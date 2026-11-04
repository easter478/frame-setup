using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FrameSetup
{
    class AdbResult
    {
        public int Code;
        public string Out = "";
        public bool TimedOut;
        public bool Ok { get { return Code == 0 && !TimedOut; } }
    }

    class AdbDevice
    {
        public string Serial, State;
        public bool Wifi { get { return Serial.Contains(":"); } }
    }

    // Runs the adb.exe that ships next to this exe (falls back to adb on PATH).
    static class Adb
    {
        public static readonly string Exe = File.Exists(Path.Combine(App.Dir, "adb.exe")) ? Path.Combine(App.Dir, "adb.exe") : "adb";

        public static Task<AdbResult> Run(string args, int timeoutMs = 15000)
        {
            return Task.Run(() =>
            {
                App.Log("> adb " + args);
                var r = new AdbResult();
                var sb = new StringBuilder();
                var psi = new ProcessStartInfo(Exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                try
                {
                    using (var p = new Process { StartInfo = psi })
                    {
                        DataReceivedEventHandler add = (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                        p.OutputDataReceived += add;
                        p.ErrorDataReceived += add;
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        // WaitForExit(int) does not wait for the pipes to close: adb start-server leaves a daemon holding them.
                        if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; try { p.Kill(); } catch { } }
                        else { System.Threading.Thread.Sleep(60); r.Code = p.ExitCode; }
                    }
                }
                catch (Exception ex) { r.Code = -1; lock (sb) sb.AppendLine(ex.Message); }
                lock (sb)
                    r.Out = string.Join("\n", sb.ToString().Replace("\r", "").Split('\n')
                        .Where(l => !l.StartsWith("* daemon")).ToArray()).Trim();
                App.Log(r.Out + (r.TimedOut ? "\n(timed out)" : ""));
                return r;
            });
        }

        public static Task<AdbResult> Shell(string serial, string cmd, int timeoutMs = 10000)
        {
            return Run("-s " + serial + " shell \"" + cmd.Replace("\"", "\\\"") + "\"", timeoutMs);
        }

        public static List<AdbDevice> ParseDevices(string output)
        {
            var list = new List<AdbDevice>();
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] != "List")
                    list.Add(new AdbDevice { Serial = parts[0], State = parts[1] });
            }
            return list;
        }

        // Is the "device not found / unauthorized / offline" kind of error: the frame locked ADB again.
        public static bool LostDevice(AdbResult r)
        {
            string o = r.Out.ToLowerInvariant();
            return o.Contains("not found") || o.Contains("unauthorized") || o.Contains("offline") || o.Contains("no devices");
        }
    }

    // Quick scan of a /24 for an open TCP 5555 (ADB over Wi-Fi).
    static class NetScan
    {
        public static List<string> LocalSubnets()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = a.Address.GetAddressBytes();
                        if (b[0] == 169) continue;
                        string net = b[0] + "." + b[1] + "." + b[2] + ".0/24";
                        if (!list.Contains(net)) list.Add(net);
                    }
                }
            }
            catch { }
            return list;
        }

        public static async Task<List<string>> Scan(string subnet, int port)
        {
            string prefix = subnet.Split('/')[0];
            prefix = prefix.Substring(0, prefix.LastIndexOf('.') + 1);
            var tasks = new List<Task<string>>();
            for (int i = 1; i < 255; i++) tasks.Add(Probe(prefix + i, port));
            var found = await Task.WhenAll(tasks);
            return found.Where(x => x != null).OrderBy(x => int.Parse(x.Substring(x.LastIndexOf('.') + 1))).ToList();
        }

        static async Task<string> Probe(string ip, int port)
        {
            using (var c = new TcpClient())
            {
                try
                {
                    var t = c.ConnectAsync(ip, port);
                    if (await Task.WhenAny(t, Task.Delay(600)) == t && c.Connected) return ip;
                }
                catch { }
            }
            return null;
        }
    }

    class FrameInfo
    {
        public string Id, Name, Ip, Version, Screen;
        public bool Online;
        public long LastSeen;
    }

    // GET {server}/api/frames → [{id, name, online, lastIp, status:{ip, version, width, height}}]
    static class Dashboard
    {
        public static Task<List<FrameInfo>> Frames(string server)
        {
            return Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create(server.TrimEnd('/') + "/api/frames");
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                string body;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                var arr = new JavaScriptSerializer { MaxJsonLength = 64 << 20 }.DeserializeObject(body) as object[];
                var list = new List<FrameInfo>();
                if (arr == null) return list;
                foreach (var o in arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var st = Get(d, "status") as Dictionary<string, object>;
                    var f = new FrameInfo
                    {
                        Id = Str(Get(d, "id")), Name = Str(Get(d, "name")), Online = Get(d, "online") is bool && (bool)Get(d, "online"),
                        Ip = Str(Get(d, "lastIp")), LastSeen = Convert.ToInt64(Get(d, "lastSeen") ?? 0)
                    };
                    if (st != null)
                    {
                        f.Version = Str(Get(st, "version"));
                        if (!string.IsNullOrEmpty(Str(Get(st, "ip")))) f.Ip = Str(Get(st, "ip"));
                        if (Get(st, "width") != null) f.Screen = Get(st, "width") + "×" + Get(st, "height");
                    }
                    list.Add(f);
                }
                return list;
            });
        }

        static object Get(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }
        static string Str(object o) { return o == null ? null : o.ToString(); }
    }
}
