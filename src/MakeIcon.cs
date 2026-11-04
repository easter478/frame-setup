using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;

namespace FrameSetup
{
    // Build-time helper: writes a multi-size PNG-frame .ico from Logo.Draw.
    static class MakeIcon
    {
        static void Main(string[] args)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int sz in sizes)
                using (var bmp = Logo.Draw(sz))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            using (var fs = File.Create(args[0]))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    w.Write((byte)(sz >= 256 ? 0 : sz));
                    w.Write((byte)(sz >= 256 ? 0 : sz));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
            if (args.Length > 1)
                using (var big = Logo.Draw(256)) big.Save(args[1], ImageFormat.Png);
        }
    }
}
