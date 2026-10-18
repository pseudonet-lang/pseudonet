param(
  [Parameter(Mandatory)][string]$Exe,
  [Parameter(Mandatory)][string]$Out,
  [int]$Frames = 90, [int]$IntervalMs = 160, [string]$SamplePng = ''
)
# Captures the window of a running app with PrintWindow (no keyboard/mouse input, works even if occluded) and
# encodes an animated GIF (own LZW encoder, palette = 255 most frequent colours + black).
$src = @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing;
using System.Drawing.Imaging; using System.IO; using System.Runtime.InteropServices; using System.Threading;

public static class Rec {
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);

  public static void Run(string exe, string outPath, int frames, int intervalMs, string samplePng) {
    Process p = Process.Start(exe);
    List<int[]> shots = new List<int[]>();
    int w = 0, h = 0;
    try {
      IntPtr hwnd = IntPtr.Zero;
      for (int i = 0; i < 100 && hwnd == IntPtr.Zero; i++) { Thread.Sleep(100); p.Refresh(); hwnd = p.MainWindowHandle; }
      if (hwnd == IntPtr.Zero) throw new Exception("no window");
      Thread.Sleep(600);
      RECT r; GetClientRect(hwnd, out r); w = r.R - r.L; h = r.B - r.T;
      for (int f = 0; f < frames; f++) {
        DateTime t0 = DateTime.Now;
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb)) {
          using (Graphics g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(hwnd, dc, 3); g.ReleaseHdc(dc); }
          BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
          int[] px = new int[w * h]; Marshal.Copy(d.Scan0, px, 0, px.Length); bmp.UnlockBits(d);
          for (int i = 0; i < px.Length; i++) px[i] &= 0xFFFFFF;
          shots.Add(px);
          if (samplePng != "" && f == frames / 2) bmp.Save(samplePng, ImageFormat.Png);
        }
        int wait = intervalMs - (int)(DateTime.Now - t0).TotalMilliseconds; if (wait > 0) Thread.Sleep(wait);
      }
    } finally { try { if (!p.HasExited) p.Kill(); } catch { } }
    Encode(shots, w, h, intervalMs / 10, outPath);
  }

  static void Encode(List<int[]> shots, int w, int h, int delayCs, string outPath) {
    Dictionary<int, int> hist = new Dictionary<int, int>();
    foreach (int[] s in shots) foreach (int c in s) { int n; hist.TryGetValue(c, out n); hist[c] = n + 1; }
    List<KeyValuePair<int, int>> top = new List<KeyValuePair<int, int>>(hist);
    top.Sort(delegate (KeyValuePair<int, int> a, KeyValuePair<int, int> b) { return b.Value.CompareTo(a.Value); });
    int[] pal = new int[256]; int pc = 0;
    for (int i = 0; i < top.Count && pc < 256; i++) pal[pc++] = top[i].Key;
    Dictionary<int, byte> map = new Dictionary<int, byte>();
    for (int i = 0; i < pc; i++) map[pal[i]] = (byte)i;
    using (FileStream fs = new FileStream(outPath, FileMode.Create)) {
      BinaryWriter bw = new BinaryWriter(fs);
      bw.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
      bw.Write((short)w); bw.Write((short)h); bw.Write((byte)0xF7); bw.Write((byte)0); bw.Write((byte)0);
      for (int i = 0; i < 256; i++) { int c = pal[i]; bw.Write((byte)((c >> 16) & 255)); bw.Write((byte)((c >> 8) & 255)); bw.Write((byte)(c & 255)); }
      bw.Write(new byte[] { 0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00 });
      foreach (int[] s in shots) {
        byte[] idx = new byte[s.Length];
        for (int i = 0; i < s.Length; i++) {
          int c = s[i]; byte b;
          if (!map.TryGetValue(c, out b)) {
            int best = int.MaxValue; int bi = 0;
            int cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
            for (int k = 0; k < pc; k++) {
              int dr = cr - ((pal[k] >> 16) & 255), dg = cg - ((pal[k] >> 8) & 255), db = cb - (pal[k] & 255);
              int dd = dr * dr + dg * dg + db * db; if (dd < best) { best = dd; bi = k; }
            }
            b = (byte)bi; map[c] = b;
          }
          idx[i] = b;
        }
        bw.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 }); bw.Write((short)delayCs); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0x2C); bw.Write((short)0); bw.Write((short)0); bw.Write((short)w); bw.Write((short)h); bw.Write((byte)0);
        bw.Write((byte)8);
        byte[] data = Lzw(idx, 8);
        int pos = 0;
        while (pos < data.Length) { int n = Math.Min(255, data.Length - pos); bw.Write((byte)n); bw.Write(data, pos, n); pos += n; }
        bw.Write((byte)0);
      }
      bw.Write((byte)0x3B);
    }
  }

  static byte[] Lzw(byte[] idx, int minCode) {
    int clear = 1 << minCode, eoi = clear + 1, codeSize = minCode + 1, next = eoi + 1;
    List<byte> o = new List<byte>(); int acc = 0, nb = 0;
    Action<int, int> emit = delegate (int code, int size) {
      acc |= code << nb; nb += size;
      while (nb >= 8) { o.Add((byte)(acc & 255)); acc >>= 8; nb -= 8; }
    };
    Dictionary<int, int> dict = new Dictionary<int, int>();
    emit(clear, codeSize);
    int prefix = idx[0];
    for (int i = 1; i < idx.Length; i++) {
      int k = idx[i]; int key = (prefix << 8) | k; int code;
      if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
      emit(prefix, codeSize);
      if (next < 4096) { dict[key] = next++; if (next > (1 << codeSize) && codeSize < 12) codeSize++; }
      else { emit(clear, codeSize); dict.Clear(); codeSize = minCode + 1; next = eoi + 1; }
      prefix = k;
    }
    emit(prefix, codeSize); emit(eoi, codeSize);
    if (nb > 0) o.Add((byte)(acc & 255));
    return o.ToArray();
  }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing
[Rec]::Run($Exe, $Out, $Frames, $IntervalMs, $SamplePng)
Write-Host ("GIF: {0} ({1:N0} KB)" -f $Out, ((Get-Item $Out).Length / 1KB))
