using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.Windows.Forms;

class GM : Form
{
    [DllImport("user32.dll")] static extern void LockWorkStation();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr i, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern void SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point lpPoint);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)] static extern uint SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

    const int SW_RESTORE = 9;
    const uint WM_COMMAND = 0x0111;
    static List<Form> overlays = new List<Form>();
    static bool focusActive = false;
    static string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    static string hostsBackup = Path.Combine(Path.GetTempPath(), "gm_hosts_backup.txt");
    static string[] blockSites = { "youtube.com", "tiktok.com", "twitter.com", "x.com", "instagram.com", "reddit.com", "facebook.com" };
    static Label statusRef;
    static DateTime focusStartTime;
    static System.Windows.Forms.Timer focusTimer;
    static string favPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gm_favourites.txt");
    static List<string> favourites = new List<string>();
    static string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gm_recent.txt");
    static List<string> recentTools = new List<string>();
    const int MaxRecent = 5;
    static Dictionary<string, int> toolUsage = new Dictionary<string, int>();
    static string statsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "gm_stats.txt");
    static DateTime sessionStart = DateTime.Now;

    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => MessageBox.Show("Error: " + e.Exception.Message, "GM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { var ex = e.ExceptionObject as Exception; if (ex != null) MessageBox.Show("Error: " + ex.Message, "GM", MessageBoxButtons.OK, MessageBoxIcon.Error); };
        LoadStats();
        var hub = new Hub();
        Application.Run(hub);
    }

    static void LoadFavourites()
    {
        favourites.Clear();
        try { if (File.Exists(favPath)) { foreach (string l in File.ReadAllLines(favPath)) { string t = l.Trim(); if (t.Length > 0) favourites.Add(t); } } } catch { }
    }

    static void SaveFavourites() { try { File.WriteAllLines(favPath, favourites.ToArray()); } catch { } }
    static bool IsFavourite(string name) { return favourites.Contains(name); }
    static void ToggleFavourite(string name) { if (favourites.Contains(name)) favourites.Remove(name); else favourites.Add(name); SaveFavourites(); }

    static void LoadRecent()
    {
        recentTools.Clear();
        try { if (File.Exists(recentPath)) { foreach (string l in File.ReadAllLines(recentPath)) { string t = l.Trim(); if (t.Length > 0) recentTools.Add(t); } } } catch { }
    }

    static void SaveRecent() { try { File.WriteAllLines(recentPath, recentTools.ToArray()); } catch { } }

    static void LoadStats()
    {
        toolUsage.Clear();
        try
        {
            if (File.Exists(statsPath))
            {
                foreach (string l in File.ReadAllLines(statsPath))
                {
                    string t = l.Trim();
                    if (t.Length == 0) continue;
                    int pipe = t.IndexOf('|');
                    if (pipe > 0)
                    {
                        string name = t.Substring(0, pipe);
                        int count;
                        if (int.TryParse(t.Substring(pipe + 1), out count))
                            toolUsage[name] = count;
                    }
                }
            }
        }
        catch { }
    }

    static void SaveStats()
    {
        try
        {
            var lines = new List<string>();
            foreach (var kv in toolUsage)
                lines.Add(kv.Key + "|" + kv.Value);
            File.WriteAllLines(statsPath, lines.ToArray());
        }
        catch { }
    }

    static void TrackUsage(string toolName)
    {
        if (toolName == null || toolName.Length == 0) return;
        if (toolUsage.ContainsKey(toolName))
            toolUsage[toolName]++;
        else
            toolUsage[toolName] = 1;
        SaveStats();
    }

    static void AddToRecent(string name)
    {
        if (recentTools.Contains(name)) recentTools.Remove(name);
        recentTools.Insert(0, name);
        while (recentTools.Count > MaxRecent) recentTools.RemoveAt(recentTools.Count - 1);
        SaveRecent();
    }

    static void RefreshFavourites(FlowLayoutPanel favPanel)
    {
        foreach (Control c in favPanel.Controls) { try { if (c.Font != null) c.Font.Dispose(); } catch { } }
        favPanel.Controls.Clear();
        foreach (string name in favourites)
        {
            var btn = new Button
            {
                Text = name,
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 40, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(80, 60, 100);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(60, 40, 80);
            favPanel.Controls.Add(btn);
        }
    }

    static void RefreshRecent(FlowLayoutPanel recentPanel, Font recentFont)
    {
        foreach (Control c in recentPanel.Controls) { try { if (c.Font != null) c.Font.Dispose(); } catch { } }
        recentPanel.Controls.Clear();
        foreach (string name in recentTools)
        {
            var btn = new Button
            {
                Text = name,
                Size = new Size(80, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White,
                Font = recentFont,
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(80, 80, 100);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(60, 60, 80);
            recentPanel.Controls.Add(btn);
        }
    }

    static void SetStatus(string text)
    {
        if (statusRef != null && !statusRef.IsDisposed)
            statusRef.Text = DateTime.Now.ToString("HH:mm:ss") + " - " + text;
    }

    static void Lock() { LockWorkStation(); }

    static void MinimizeAll()
    {
        try
        {
            IntPtr hWndProgman = FindWindow("Progman", null);
            if (hWndProgman != IntPtr.Zero)
                SendMessage(hWndProgman, WM_COMMAND, (IntPtr)0x0335, IntPtr.Zero);
        }
        catch { }
    }

    static void CleanTemp()
    {
        if (MessageBox.Show("This will delete ALL files in your temp folder.\nOther applications may be affected.\n\nContinue?", "GM", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        int fileCount = 0, dirCount = 0;
        try
        {
            string temp = Path.GetTempPath();
            foreach (string f in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(f); fileCount++; } catch { }
            }
            var dirs = Directory.GetDirectories(temp, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToArray();
            foreach (string d in dirs)
            {
                try { Directory.Delete(d, false); dirCount++; } catch { }
            }
        }
        catch { }
        int total = fileCount + dirCount;
        MessageBox.Show("Cleaned " + total + " items (" + fileCount + " files, " + dirCount + " folders) from temp.", "GM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SetStatus("Temp cleaned: " + total + " items");
    }

    static void ToggleDark()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true))
            {
                if (k == null) { MessageBox.Show("Could not access registry.", "GM"); return; }
                int v = (int)k.GetValue("AppsUseLightTheme", 1);
                k.SetValue("AppsUseLightTheme", v == 1 ? 0 : 1);
                MessageBox.Show(v == 1 ? "Dark mode ON" : "Light mode ON", "GM");
                SetStatus(v == 1 ? "Dark mode ON" : "Light mode ON");
            }
        }
        catch { MessageBox.Show("Failed to toggle dark mode.", "GM"); }
    }

    static void ToggleMic()
    {
        try
        {
            Process.Start(new ProcessStartInfo("mmsys.cpl") { UseShellExecute = true });
            MessageBox.Show("Opening Sound settings.\nRight-click your microphone -> Properties -> Levels to mute.", "GM");
            SetStatus("Sound settings opened");
        }
        catch { MessageBox.Show("Could not open Sound settings.", "GM"); }
    }

    static void LaunchAsAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath);
            psi.Verb = "runas";
            Process.Start(psi);
            Application.Exit();
        }
        catch { MessageBox.Show("Could not restart as administrator.", "GM"); }
    }

    static void ToggleFocus()
    {
        try
        {
            if (!focusActive)
            {
                if (File.Exists(hostsPath)) File.Copy(hostsPath, hostsBackup, true);
                string existing = File.Exists(hostsPath) ? File.ReadAllText(hostsPath) : "";
                if (!existing.Contains("# GM Focus Mode"))
                {
                    string block = "\n# GM Focus Mode\n";
                    foreach (string s in blockSites)
                        block += "127.0.0.1 " + s + "\n127.0.0.1 www." + s + "\n";
                    File.WriteAllText(hostsPath, existing + block);
                }
                FlushDns();
                focusActive = true;
                focusStartTime = DateTime.Now;
                if (focusTimer != null) focusTimer.Start();
                MessageBox.Show("Focus mode ON\nBlocked: " + string.Join(", ", blockSites), "GM");
                SetStatus("Focus mode ON");
            }
            else
            {
                if (File.Exists(hostsBackup)) File.Copy(hostsBackup, hostsPath, true);
                else File.WriteAllText(hostsPath, "");
                FlushDns();
                focusActive = false;
                if (focusTimer != null) focusTimer.Stop();
                MessageBox.Show("Focus mode OFF", "GM");
                SetStatus("Focus mode OFF");
            }
        }
        catch { MessageBox.Show("Focus mode requires admin.\nRight-click gm.exe -> Run as administrator.", "GM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    static void FlushDns()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            if (p != null) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
        }
        catch { }
    }

    static void EnableClip()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Clipboard", true))
            {
                if (k == null)
                {
                    using (var nk = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Clipboard"))
                    {
                        if (nk != null) nk.SetValue("EnableClipboardHistory", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                else
                {
                    k.SetValue("EnableClipboardHistory", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            MessageBox.Show("Clipboard history enabled!\nPress Win+V to view.", "GM");
            SetStatus("Clipboard history enabled");
        }
        catch { MessageBox.Show("Could not enable clipboard history.", "GM"); }
    }

    static void Screenshot()
    {
        Bitmap bmp = null;
        try
        {
            var screens = Screen.AllScreens;
            int w = 0, h = 0;
            foreach (var s in screens)
            {
                w = Math.Max(w, s.Bounds.Right);
                h = Math.Max(h, s.Bounds.Bottom);
            }
            bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                foreach (var s in screens)
                    g.CopyFromScreen(s.Bounds.Location, s.Bounds.Location, s.Bounds.Size);
            }
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            bmp.Save(p, ImageFormat.Png);
            MessageBox.Show("Screenshot saved to Desktop.", "GM");
            SetStatus("Screenshot saved");
        }
        catch { MessageBox.Show("Screenshot failed.", "GM"); }
        finally { if (bmp != null) bmp.Dispose(); }
    }

    static void ScreenshotToClipboard()
    {
        Bitmap bmp = null;
        try
        {
            var screens = Screen.AllScreens;
            int w = 0, h = 0;
            foreach (var s in screens)
            {
                w = Math.Max(w, s.Bounds.Right);
                h = Math.Max(h, s.Bounds.Bottom);
            }
            bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                foreach (var s in screens)
                    g.CopyFromScreen(s.Bounds.Location, s.Bounds.Location, s.Bounds.Size);
            }
            Clipboard.SetImage(bmp);
            MessageBox.Show("Screenshot copied to clipboard.", "GM");
            SetStatus("Screenshot copied to clipboard");
        }
        catch { MessageBox.Show("Screenshot failed.", "GM"); }
        finally { if (bmp != null) bmp.Dispose(); }
    }

    static string FindAppPath(string exeName)
    {
        string path = Environment.GetEnvironmentVariable("PATH");
        if (path != null)
        {
            foreach (string dir in path.Split(';'))
            {
                try
                {
                    string full = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }
        string[] roots = {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        foreach (string root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (string d in Directory.GetDirectories(root))
                {
                    try
                    {
                        string full = Path.Combine(d, exeName);
                        if (File.Exists(full)) return full;
                    }
                    catch { }
                }
            }
            catch { }
        }
        return exeName;
    }

    static void LaunchApps()
    {
        string launched = "";
        string[] apps = { "chrome.exe", "discord.exe", "spotify.exe" };
        foreach (string a in apps)
        {
            try
            {
                string path = FindAppPath(a);
                var p = Process.Start(path);
                if (p != null) { p.Dispose(); launched += a.Replace(".exe", "") + " "; }
            }
            catch { }
        }
        if (launched.Length > 0)
        {
            MessageBox.Show("Launched: " + launched.Trim(), "GM");
            SetStatus("Launched: " + launched.Trim());
        }
        else
        {
            MessageBox.Show("Could not launch any apps.", "GM");
            SetStatus("Launch failed");
        }
    }

    static void StartPingOverlay()
    {
        foreach (Form o in overlays)
        {
            if (o.Text == "Ping" && !o.IsDisposed) { o.BringToFront(); return; }
        }
        Screen s = Screen.PrimaryScreen;
        int sx = s != null ? s.WorkingArea.Width - 240 : 500;
        var f = new Form
        {
            Text = "Ping",
            Size = new Size(220, 70),
            FormBorderStyle = FormBorderStyle.None,
            BackColor = Color.Black,
            Opacity = 0.85,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(sx, 10)
        };
        var lbl = new Label { Dock = DockStyle.Fill, ForeColor = Color.Lime, Font = new Font("Consolas", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Text = "Pinging..." };
        bool dragging = false;
        int dragOffX = 0, dragOffY = 0;
        lbl.MouseDown += (ms, me) =>
        {
            if (me.Button == MouseButtons.Right) { f.Close(); return; }
            if (me.Button == MouseButtons.Left) { dragging = true; dragOffX = me.X; dragOffY = me.Y; lbl.Capture = false; }
        };
        lbl.MouseMove += (ms, me) => { if (dragging && me.Button == MouseButtons.Left) f.Location = new Point(f.Location.X + me.X - dragOffX, f.Location.Y + me.Y - dragOffY); };
        lbl.MouseUp += (ms, me) => { dragging = false; };
        f.Controls.Add(lbl);
        Ping ping = null;
        int pingingInt = 0;
        bool pingClosed = false;
        var t = new System.Windows.Forms.Timer { Interval = 2000 };
        t.Tick += (ts, te) =>
        {
            if (pingClosed) return;
            if (System.Threading.Interlocked.CompareExchange(ref pingingInt, 1, 0) != 0) return;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (pingClosed) { System.Threading.Interlocked.Exchange(ref pingingInt, 0); return; }
                    if (ping == null) ping = new Ping();
                    var r = ping.Send("8.8.8.8", 1000);
                    try
                    {
                        f.Invoke((Action)(() =>
                        {
                            if (r.Status == IPStatus.Success)
                            {
                                lbl.Text = "PING: " + r.RoundtripTime + "ms";
                                lbl.ForeColor = r.RoundtripTime < 50 ? Color.Lime : r.RoundtripTime < 100 ? Color.Yellow : Color.Red;
                            }
                            else
                            {
                                lbl.Text = "PING: TIMEOUT";
                                lbl.ForeColor = Color.Red;
                            }
                            pingingInt = 0;
                        }));
                    }
                    catch { System.Threading.Interlocked.Exchange(ref pingingInt, 0); }
                }
                catch
                {
                    try { f.Invoke((Action)(() => { lbl.Text = "PING: ERROR"; lbl.ForeColor = Color.Red; pingingInt = 0; })); } catch { System.Threading.Interlocked.Exchange(ref pingingInt, 0); }
                }
            });
        };
        t.Start();
        f.FormClosed += (fs, fe) => { pingClosed = true; t.Stop(); t.Dispose(); System.Threading.Thread.Sleep(50); if (ping != null) { try { ping.Dispose(); } catch { } } lbl.Font.Dispose(); lbl.Dispose(); overlays.Remove(f); };
        overlays.Add(f);
        f.Show();
    }

    static void StartVolumeControl()
    {
        try { Process.Start("sndvol.exe"); }
        catch { MessageBox.Show("Could not open Volume Mixer.", "GM"); }
    }

    static void ShowNetworkInfo()
    {
        try
        {
            string info = "";
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                info += nic.Name + "\n";
                info += "  Type: " + nic.NetworkInterfaceType + "\n";
                info += "  Speed: " + (nic.Speed / 1000000) + " Mbps\n";
                var ip = nic.GetIPProperties();
                if (ip.GatewayAddresses.Count > 0)
                    info += "  Gateway: " + ip.GatewayAddresses[0].Address + "\n";
                foreach (var addr in ip.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        info += "  IPv4: " + addr.Address + "\n";
                }
                if (ip.DnsAddresses.Count > 0)
                    info += "  DNS: " + ip.DnsAddresses[0] + "\n";
                info += "\n";
            }
            if (info.Length == 0) info = "No active network interfaces found.";
            MessageBox.Show(info.Trim(), "GM - Network Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("Network info displayed");
        }
        catch { MessageBox.Show("Could not retrieve network info.", "GM"); }
    }

    static void ShowSystemInfo()
    {
        try
        {
            var pc = System.Diagnostics.Process.GetCurrentProcess();
            try
            {
                string info = "";
                info += "OS: " + Environment.OSVersion.VersionString + "\n";
                info += "64-bit: " + Environment.Is64BitOperatingSystem + "\n";
                info += "Processor Count: " + Environment.ProcessorCount + "\n";
                info += "Working Set: " + (pc.WorkingSet64 / 1024 / 1024) + " MB\n";
                info += "Uptime: " + TimeSpan.FromMilliseconds(Environment.TickCount).ToString(@"dd\.hh\:mm\:ss") + "\n";
                info += "\n";
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        long freeGB = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                        long totalGB = drive.TotalSize / 1024 / 1024 / 1024;
                        info += drive.Name + " " + drive.VolumeLabel + "\n";
                        info += "  " + freeGB + " GB free / " + totalGB + " GB total\n";
                    }
                }
                MessageBox.Show(info.Trim(), "GM - System Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("System info displayed");
            }
            catch { MessageBox.Show("Could not retrieve system info.", "GM"); }
            finally { try { pc.Dispose(); } catch { } }
        }
        catch { MessageBox.Show("Could not retrieve system info.", "GM"); }
    }

    static void EmptyRecycleBin()
    {
        if (MessageBox.Show("Empty the Recycle Bin?", "GM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            uint result = SHEmptyRecycleBin(IntPtr.Zero, null, 7);
            if (result == 0)
            {
                MessageBox.Show("Recycle Bin emptied.", "GM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("Recycle Bin emptied");
            }
            else
            {
                MessageBox.Show("Could not empty Recycle Bin. Error: " + result, "GM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch { MessageBox.Show("Could not empty Recycle Bin.", "GM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    static string LayoutWork()
    {
        Screen s = Screen.PrimaryScreen;
        if (s == null) return "No screen found.";
        int w = s.WorkingArea.Width / 2;
        int h = s.WorkingArea.Height;
        string result = "";
        try
        {
            bool foundChrome = false;
            foreach (Process p in Process.GetProcessesByName("chrome"))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                        SetWindowPos(p.MainWindowHandle, IntPtr.Zero, 0, 0, w, h, 0x0040);
                        foundChrome = true;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            bool foundDiscord = false;
            foreach (Process p in Process.GetProcessesByName("discord"))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                        SetWindowPos(p.MainWindowHandle, IntPtr.Zero, w, 0, w, h, 0x0040);
                        foundDiscord = true;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (foundChrome && foundDiscord) result = "Snapped Chrome + Discord";
            else if (foundChrome) result = "Snapped Chrome (Discord not found)";
            else if (foundDiscord) result = "Snapped Discord (Chrome not found)";
            else result = "No Chrome or Discord windows found";
        }
        catch { result = "Layout failed"; }
        return result;
    }

    // ==================== EMBEDDED TOOLS ====================

    static void OpenTimer()
    {
        var f = new Form();
        f.Text = "GM - Timer";
        f.Size = new Size(360, 240);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var sw = new Stopwatch();
        var ticker = new System.Windows.Forms.Timer();
        string lastLap = "";

        var lblTime = new Label
        {
            Text = "00:00:00.00",
            Font = new Font("Consolas", 32, FontStyle.Bold),
            ForeColor = Color.Lime,
            AutoSize = false,
            Size = new Size(340, 60),
            Location = new Point(5, 10),
            TextAlign = ContentAlignment.MiddleCenter
        };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var btnStart = new Button { Text = "Start", Location = new Point(10, 80), Size = new Size(75, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 160, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStart.FlatAppearance.BorderSize = 0;
        btnStart.Click += (s, e) => { sw.Start(); ticker.Start(); };
        var btnStop = new Button { Text = "Stop", Location = new Point(95, 80), Size = new Size(75, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStop.FlatAppearance.BorderSize = 0;
        btnStop.Click += (s, e) => { sw.Stop(); ticker.Stop(); };
        var btnLap = new Button { Text = "Lap", Location = new Point(180, 80), Size = new Size(75, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLap.FlatAppearance.BorderSize = 0;
        btnLap.Click += (s, e) =>
        {
            if (sw.IsRunning)
            {
                TimeSpan ts = sw.Elapsed;
                string lap = string.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                if (lastLap.Length > 0) lblTime.Text = lap + "  (last: " + lastLap + ")";
                else lblTime.Text = lap;
                lastLap = lap;
            }
        };
        var btnReset = new Button { Text = "Reset", Location = new Point(265, 80), Size = new Size(75, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnReset.FlatAppearance.BorderSize = 0;
        btnReset.Click += (s, e) => { sw.Reset(); ticker.Stop(); lblTime.Text = "00:00:00.00"; lastLap = ""; };
        var btnCopy = new Button { Text = "Copy", Location = new Point(140, 120), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Click += (s, e) => { try { Clipboard.SetText(lblTime.Text); } catch { } };

        var lblHint = new Label { Text = "Space=Start/Stop  R=Reset  L=Lap  Ctrl+C=Copy", Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(55, 160) };

        ticker.Interval = 10;
        ticker.Tick += (s, e) =>
        {
            TimeSpan ts = sw.Elapsed;
            lblTime.Text = string.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
        };

        f.KeyPreview = true;
        f.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Space) { if (sw.IsRunning) { sw.Stop(); ticker.Stop(); } else { sw.Start(); ticker.Start(); } }
            if (e.KeyCode == Keys.R) { sw.Reset(); ticker.Stop(); lblTime.Text = "00:00:00.00"; lastLap = ""; }
            if (e.KeyCode == Keys.L && sw.IsRunning) btnLap.PerformClick();
            if (e.Control && e.KeyCode == Keys.C) { try { Clipboard.SetText(lblTime.Text); } catch { } }
        };

        f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); btnFont.Dispose(); lblTime.Font.Dispose(); lblHint.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblTime, btnStart, btnStop, btnLap, btnReset, btnCopy, lblHint });
        f.Show();
        SetStatus("Timer opened");
    }

    static void OpenColorPicker()
    {
        var f = new Form();
        f.Text = "GM - Color Picker";
        f.Size = new Size(300, 230);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico2 = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico2;

        string lastHex = "#000000";
        string lastRgb = "0, 0, 0";

        var lblPreview = new Label { Size = new Size(270, 40), Location = new Point(10, 10), BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
        var lblHex = new Label { Text = "Hex: #000000", Font = new Font("Consolas", 11, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 58) };
        var lblRgb = new Label { Text = "RGB: 0, 0, 0", Font = new Font("Consolas", 11, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 80) };

        var btnCopyHex = new Button { Text = "Copy Hex", Location = new Point(10, 108), Size = new Size(130, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
        btnCopyHex.FlatAppearance.BorderSize = 0;
        btnCopyHex.Click += (s, e) => { try { Clipboard.SetText(lastHex); btnCopyHex.Text = "Copied!"; } catch { } };
        btnCopyHex.MouseLeave += (s, e) => btnCopyHex.Text = "Copy Hex";

        var btnCopyRgb = new Button { Text = "Copy RGB", Location = new Point(150, 108), Size = new Size(130, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
        btnCopyRgb.FlatAppearance.BorderSize = 0;
        btnCopyRgb.Click += (s, e) => { try { Clipboard.SetText(lastRgb); btnCopyRgb.Text = "Copied!"; } catch { } };
        btnCopyRgb.MouseLeave += (s, e) => btnCopyRgb.Text = "Copy RGB";

        var lblHint = new Label { Text = "Hover pixel to pick | Ctrl+C = Hex | Ctrl+Shift+C = RGB", Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(20, 150) };

        var poll = new System.Windows.Forms.Timer();
        poll.Interval = 100;
        poll.Tick += (s, e) =>
        {
            Point p;
            GetCursorPos(out p);
            IntPtr hdc = GetDC(IntPtr.Zero); if (hdc == IntPtr.Zero) return;
            uint pixel = GetPixel(hdc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, hdc);
            Color c = Color.FromArgb((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF));
            lblPreview.BackColor = c;
            lastHex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            lastRgb = c.R + ", " + c.G + ", " + c.B;
            lblHex.Text = "Hex: " + lastHex;
            lblRgb.Text = "RGB: " + lastRgb;
        };
        poll.Start();

        f.KeyPreview = true;
        f.KeyDown += (s, e) =>
        {
            if (e.Control && !e.Shift && e.KeyCode == Keys.C) { try { Clipboard.SetText(lastHex); } catch { } }
            if (e.Control && e.Shift && e.KeyCode == Keys.C) { try { Clipboard.SetText(lastRgb); } catch { } }
        };

        f.FormClosed += (s, e) => { poll.Stop(); poll.Dispose(); lblHex.Font.Dispose(); lblRgb.Font.Dispose(); btnCopyHex.Font.Dispose(); btnCopyRgb.Font.Dispose(); lblHint.Font.Dispose(); ico2.Dispose(); };
        f.Controls.AddRange(new Control[] { lblPreview, lblHex, lblRgb, btnCopyHex, btnCopyRgb, lblHint });
        f.Show();
        SetStatus("Color Picker opened");
    }

    static void OpenHistoryCleaner()
    {
        var f = new Form();
        f.Text = "GM - History Cleaner";
        f.Size = new Size(380, 240);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico3 = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico3;

        var lblStatus = new Label { Text = "Select a browser to clean", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(100, 15) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var btnChrome = new Button { Text = "Chrome", Location = new Point(20, 55), Size = new Size(100, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 200), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnChrome.FlatAppearance.BorderSize = 0;
        btnChrome.Click += (s, e) =>
        {
            try
            {
                var procs = Process.GetProcessesByName("chrome");
                if (procs.Length > 0)
                {
                    if (MessageBox.Show("Close Google Chrome to clean history?", "History Cleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { foreach (Process p in procs) { try { p.Dispose(); } catch { } } return; }
                    foreach (Process p in procs) { try { p.Kill(); } catch { } }
                    foreach (Process p in procs) { try { p.WaitForExit(2000); } catch { } finally { try { p.Dispose(); } catch { } } }
                }
                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
                int count = 0;
                if (Directory.Exists(userData))
                {
                    foreach (string profileDir in Directory.GetDirectories(userData))
                    {
                        string historyPath = Path.Combine(profileDir, "History");
                        if (File.Exists(historyPath)) { try { File.Delete(historyPath); count++; } catch { } }
                        if (File.Exists(historyPath + "-journal")) { try { File.Delete(historyPath + "-journal"); } catch { } }
                        if (File.Exists(historyPath + "-wal")) { try { File.Delete(historyPath + "-wal"); } catch { } }
                        if (File.Exists(historyPath + "-shm")) { try { File.Delete(historyPath + "-shm"); } catch { } }
                    }
                }
                lblStatus.Text = count > 0 ? "Chrome: history cleaned (" + count + " profiles)" : "Chrome: no history found";
            }
            catch { lblStatus.Text = "Chrome: cleanup failed"; }
        };

        var btnEdge = new Button { Text = "Edge", Location = new Point(140, 55), Size = new Size(100, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnEdge.FlatAppearance.BorderSize = 0;
        btnEdge.Click += (s, e) =>
        {
            try
            {
                var procs = Process.GetProcessesByName("msedge");
                if (procs.Length > 0)
                {
                    if (MessageBox.Show("Close Microsoft Edge to clean history?", "History Cleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { foreach (Process p in procs) { try { p.Dispose(); } catch { } } return; }
                    foreach (Process p in procs) { try { p.Kill(); } catch { } }
                    foreach (Process p in procs) { try { p.WaitForExit(2000); } catch { } finally { try { p.Dispose(); } catch { } } }
                }
                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data");
                int count = 0;
                if (Directory.Exists(userData))
                {
                    foreach (string profileDir in Directory.GetDirectories(userData))
                    {
                        string historyPath = Path.Combine(profileDir, "History");
                        if (File.Exists(historyPath)) { try { File.Delete(historyPath); count++; } catch { } }
                        if (File.Exists(historyPath + "-journal")) { try { File.Delete(historyPath + "-journal"); } catch { } }
                        if (File.Exists(historyPath + "-wal")) { try { File.Delete(historyPath + "-wal"); } catch { } }
                        if (File.Exists(historyPath + "-shm")) { try { File.Delete(historyPath + "-shm"); } catch { } }
                    }
                }
                lblStatus.Text = count > 0 ? "Edge: history cleaned (" + count + " profiles)" : "Edge: no history found";
            }
            catch { lblStatus.Text = "Edge: cleanup failed"; }
        };

        var btnFirefox = new Button { Text = "Firefox", Location = new Point(260, 55), Size = new Size(100, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(180, 80, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnFirefox.FlatAppearance.BorderSize = 0;
        btnFirefox.Click += (s, e) =>
        {
            try
            {
                var procs = Process.GetProcessesByName("firefox");
                if (procs.Length > 0)
                {
                    if (MessageBox.Show("Close Firefox to clean history?", "History Cleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { foreach (Process p in procs) { try { p.Dispose(); } catch { } } return; }
                    foreach (Process p in procs) { try { p.Kill(); } catch { } }
                    foreach (Process p in procs) { try { p.WaitForExit(2000); } catch { } finally { try { p.Dispose(); } catch { } } }
                }
                string profilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
                int count = 0;
                if (Directory.Exists(profilesDir))
                {
                    foreach (string profileDir in Directory.GetDirectories(profilesDir))
                    {
                        string historyPath = Path.Combine(profileDir, "places.sqlite");
                        if (File.Exists(historyPath)) { try { File.Delete(historyPath); count++; } catch { } }
                        if (File.Exists(historyPath + "-journal")) { try { File.Delete(historyPath + "-journal"); } catch { } }
                    }
                }
                lblStatus.Text = count > 0 ? "Firefox: history cleaned (" + count + " profiles)" : "Firefox: no history found";
            }
            catch { lblStatus.Text = "Firefox: cleanup failed"; }
        };
        
        var btnAll = new Button { Text = "Clean All", Location = new Point(130, 105), Size = new Size(100, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAll.FlatAppearance.BorderSize = 0;
        btnAll.Click += (s, e) => { btnChrome.PerformClick(); btnEdge.PerformClick(); btnFirefox.PerformClick(); };

        var lblHint = new Label { Text = "Browsers will be closed before cleaning", Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(95, 155) };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblStatus.Font.Dispose(); lblHint.Font.Dispose(); ico3.Dispose(); };
        f.Controls.AddRange(new Control[] { lblStatus, btnChrome, btnEdge, btnFirefox, btnAll, lblHint });
        f.Show();
        SetStatus("History Cleaner opened");
    }

    static void OpenShutdownTimer()
    {
        var f = new Form();
        f.Text = "GM - Shutdown Timer";
        f.Size = new Size(340, 270);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico4 = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico4;

        var ticker = new System.Windows.Forms.Timer();
        int secondsLeft = 0;
        bool isRestart = false;
        bool shutdownScheduled = false;

        var lblTitle = new Label { Text = "Shutdown Timer", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(95, 10) };
        var lblInput = new Label { Text = "Minutes:", Font = new Font("Segoe UI", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 50) };
        var txtMinutes = new TextBox { Font = new Font("Consolas", 14), Size = new Size(100, 30), Location = new Point(100, 47), Text = "30", TextAlign = HorizontalAlignment.Center, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblCountdown = new Label { Text = "", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.Lime, AutoSize = false, Size = new Size(300, 50), Location = new Point(10, 85), TextAlign = ContentAlignment.MiddleCenter };
        var lblStatus = new Label { Text = "Enter minutes and click Start", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = true, Location = new Point(80, 140) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var btnStart = new Button { Text = "Shutdown", Location = new Point(20, 175), Size = new Size(95, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStart.FlatAppearance.BorderSize = 0;
        var btnRestart = new Button { Text = "Restart", Location = new Point(130, 175), Size = new Size(95, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 120, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRestart.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button { Text = "Cancel", Location = new Point(240, 175), Size = new Size(95, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Enabled = false;

        btnStart.Click += (s, e) =>
        {
            int mins;
            if (!int.TryParse(txtMinutes.Text, out mins) || mins <= 0 || mins > 1440) { lblStatus.Text = "Enter 1-1440 minutes"; return; }
            if (MessageBox.Show("Shut down PC in " + mins + " minute(s)?", "Shutdown Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            isRestart = false; secondsLeft = mins * 60; btnStart.Enabled = false; btnRestart.Enabled = false; btnCancel.Enabled = true; txtMinutes.Enabled = false;
            lblStatus.Text = "Shutdown scheduled"; lblCountdown.ForeColor = Color.Lime; ticker.Start();
            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t " + secondsLeft) { CreateNoWindow = true, UseShellExecute = false }); shutdownScheduled = true; }
            catch { lblStatus.Text = "Failed"; ticker.Stop(); btnStart.Enabled = true; btnRestart.Enabled = true; btnCancel.Enabled = false; txtMinutes.Enabled = true; }
        };
        btnRestart.Click += (s, e) =>
        {
            int mins;
            if (!int.TryParse(txtMinutes.Text, out mins) || mins <= 0 || mins > 1440) { lblStatus.Text = "Enter 1-1440 minutes"; return; }
            if (MessageBox.Show("Restart PC in " + mins + " minute(s)?", "Shutdown Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            isRestart = true; secondsLeft = mins * 60; btnStart.Enabled = false; btnRestart.Enabled = false; btnCancel.Enabled = true; txtMinutes.Enabled = false;
            lblStatus.Text = "Restart scheduled"; lblCountdown.ForeColor = Color.FromArgb(255, 165, 0); ticker.Start();
            try { Process.Start(new ProcessStartInfo("shutdown", "/r /t " + secondsLeft) { CreateNoWindow = true, UseShellExecute = false }); shutdownScheduled = true; }
            catch { lblStatus.Text = "Failed"; ticker.Stop(); btnStart.Enabled = true; btnRestart.Enabled = true; btnCancel.Enabled = false; txtMinutes.Enabled = true; }
        };
        btnCancel.Click += (s, e) =>
        {
            ticker.Stop();
            try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            shutdownScheduled = false;
            lblCountdown.Text = ""; lblStatus.Text = "Cancelled"; f.Text = "GM - Shutdown Timer";
            btnStart.Enabled = true; btnRestart.Enabled = true; btnCancel.Enabled = false; txtMinutes.Enabled = true;
        };

        ticker.Interval = 1000;
        ticker.Tick += (s, e) =>
        {
            if (secondsLeft <= 0) { ticker.Stop(); lblCountdown.Text = isRestart ? "RESTART" : "SHUTDOWN"; f.Text = "GM - Shutdown Timer"; return; }
            secondsLeft--;
            int h = secondsLeft / 3600; int m = (secondsLeft % 3600) / 60; int sec = secondsLeft % 60;
            lblCountdown.Text = string.Format("{0:00}:{1:00}:{2:00}", h, m, sec);
            f.Text = string.Format("GM - Shutdown Timer - {0}", lblCountdown.Text);
        };

        f.KeyPreview = true;
        f.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape && btnCancel.Enabled)
            {
                ticker.Stop();
                try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                shutdownScheduled = false;
                lblCountdown.Text = ""; lblStatus.Text = "Cancelled"; f.Text = "GM - Shutdown Timer";
                btnStart.Enabled = true; btnRestart.Enabled = true; btnCancel.Enabled = false; txtMinutes.Enabled = true;
            }
            if (e.KeyCode == Keys.Enter && btnStart.Enabled) btnStart.PerformClick();
        };

        f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); btnFont.Dispose(); lblTitle.Font.Dispose(); lblInput.Font.Dispose(); txtMinutes.Font.Dispose(); lblCountdown.Font.Dispose(); lblStatus.Font.Dispose(); ico4.Dispose(); if (shutdownScheduled) try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); } catch { } };
        f.Controls.AddRange(new Control[] { lblTitle, lblInput, txtMinutes, lblCountdown, lblStatus, btnStart, btnRestart, btnCancel });
        f.Show();
        SetStatus("Shutdown Timer opened");
    }

    static void OpenMatrixRain()
    {
        var f = new Form();
        f.Text = "GM - Matrix Rain";
        f.FormBorderStyle = FormBorderStyle.None;
        f.WindowState = FormWindowState.Maximized;
        f.BackColor = Color.Black;
        var _dp = typeof(Form).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); if (_dp != null) _dp.SetValue(f, true);
        f.TopMost = true;
        var ico5 = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico5;
        try { Cursor.Hide(); } catch { }

        int fontSize = 14;
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@#$%^&*()";
        Random rng = new Random();
        Font matrixFont = new Font("Consolas", fontSize);
        int[] drops;
        bool isFull = true;

        Screen _ps = Screen.PrimaryScreen; int cols = _ps != null ? _ps.Bounds.Width / fontSize : 80;
        drops = new int[cols];
        for (int i = 0; i < cols; i++) drops[i] = rng.Next(-30, 0);

        var ticker = new Timer();
        ticker.Interval = 35;
        ticker.Tick += (s, e) => { f.Invalidate(); };
        ticker.Start();

        f.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (isFull)
                {
                    Cursor.Show(); f.TopMost = false; f.FormBorderStyle = FormBorderStyle.Sizable;
                    f.WindowState = FormWindowState.Normal; f.Size = new Size(600, 400);
                    f.StartPosition = FormStartPosition.CenterScreen; isFull = false;
                    int c2 = f.ClientSize.Width / fontSize; drops = new int[c2];
                    for (int i = 0; i < c2; i++) drops[i] = rng.Next(-30, 0);
                }
                else { f.Close(); }
            }
            if (e.KeyCode == Keys.F11)
            {
                if (isFull)
                {
                    Cursor.Show(); f.TopMost = false; f.FormBorderStyle = FormBorderStyle.Sizable;
                    f.WindowState = FormWindowState.Normal; f.Size = new Size(600, 400);
                    f.StartPosition = FormStartPosition.CenterScreen; isFull = false;
                    int c2 = f.ClientSize.Width / fontSize; drops = new int[c2];
                    for (int i = 0; i < c2; i++) drops[i] = rng.Next(-30, 0);
                }
                else
                {
                    f.FormBorderStyle = FormBorderStyle.None; f.WindowState = FormWindowState.Maximized;
                    f.TopMost = true; try { Cursor.Hide(); } catch { } isFull = true;
                    Screen _ps2 = Screen.PrimaryScreen; int c2 = _ps2 != null ? _ps2.Bounds.Width / fontSize : 80; drops = new int[c2];
                    for (int i = 0; i < c2; i++) drops[i] = rng.Next(-30, 0);
                }
            }
        };
        f.Paint += (s, e) =>
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            int c = f.ClientSize.Width / fontSize;
            for (int i = 0; i < c && i < drops.Length; i++)
            {
                int x = i * fontSize;
                int y = drops[i] * fontSize;
                string ch = chars[rng.Next(chars.Length)].ToString();
                Brush head = new SolidBrush(Color.FromArgb(255, 255, 255));
                g.DrawString(ch, matrixFont, head, x, y);
                head.Dispose();
                for (int j = 1; j < 12; j++)
                {
                    int trailY = y - j * fontSize;
                    if (trailY < 0) break;
                    int alpha = 255 - (j * 10);
                    if (alpha < 10) break;
                    Brush tail = new SolidBrush(Color.FromArgb(alpha, 0, 255, 0));
                    g.DrawString(chars[rng.Next(chars.Length)].ToString(), matrixFont, tail, x, trailY);
                    tail.Dispose();
                }
                drops[i]++;
                if (drops[i] * fontSize > f.ClientSize.Height && rng.Next(100) > 93)
                    drops[i] = rng.Next(-30, 0);
            }
        };

        f.FormClosed += (s, e) => { try { ticker.Stop(); ticker.Dispose(); } catch { } try { matrixFont.Dispose(); } catch { } try { ico5.Dispose(); } catch { } Cursor.Show(); };
        f.Show();
        SetStatus("Matrix Rain opened");
    }

    // ==================== NEW FEATURES ====================

    static void OpenCpuMonitor()
    {
        var f = new Form();
        f.Text = "GM - CPU Monitor";
        f.Size = new Size(320, 200);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblCpu = new Label { Text = "CPU: 0%", Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.Lime, AutoSize = true, Location = new Point(20, 15) };
        var lblRam = new Label { Text = "RAM: 0%", Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.Cyan, AutoSize = true, Location = new Point(20, 50) };
        var lblDetail = new Label { Text = "", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(20, 90) };
        var barCpu = new ProgressBar { Location = new Point(20, 115), Size = new Size(270, 18), Style = ProgressBarStyle.Continuous };
        var barRam = new ProgressBar { Location = new Point(20, 140), Size = new Size(270, 18), Style = ProgressBarStyle.Continuous };

        var perfCpu = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
        var perfRam = new System.Diagnostics.PerformanceCounter("Memory", "% Committed Bytes In Use");
        perfCpu.NextValue();
        perfRam.NextValue();

        var ticker = new System.Windows.Forms.Timer { Interval = 1500 };
        ticker.Tick += (s, e) =>
        {
            try
            {
                float cpu = perfCpu.NextValue();
                float ram = perfRam.NextValue();
                lblCpu.Text = "CPU: " + cpu.ToString("F1") + "%";
                lblRam.Text = "RAM: " + ram.ToString("F1") + "%";
                barCpu.Value = Math.Min((int)cpu, 100);
                barRam.Value = Math.Min((int)ram, 100);
                lblCpu.ForeColor = cpu < 50 ? Color.Lime : cpu < 80 ? Color.Yellow : Color.Red;
                lblRam.ForeColor = ram < 60 ? Color.Cyan : ram < 85 ? Color.Yellow : Color.Red;
                long usedMb = GC.GetTotalMemory(false) / 1024 / 1024;
                int procCount = 0;
                try
                {
                    var procs = Process.GetProcesses();
                    procCount = procs.Length;
                    foreach (var p in procs) { try { p.Dispose(); } catch { } }
                }
                catch { }
                lblDetail.Text = "Processes: " + procCount + " | GM RAM: " + usedMb + " MB";
            }
            catch { }
        };
        ticker.Start();

        f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); perfCpu.Dispose(); perfRam.Dispose(); lblCpu.Font.Dispose(); lblRam.Font.Dispose(); lblDetail.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblCpu, lblRam, lblDetail, barCpu, barRam });
        f.Show();
        SetStatus("CPU Monitor opened");
    }

    static void ShowWifiPasswords()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show profiles");
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            var p = Process.Start(psi);
            if (p == null) { MessageBox.Show("Could not run netsh.", "GM"); return; }
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            p.Dispose();

            string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string profiles = "";
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("All User Profile"))
                {
                    int colon = trimmed.IndexOf(':');
                    if (colon >= 0) profiles += trimmed.Substring(colon + 1).Trim() + "\n";
                }
            }

            if (profiles.Length == 0)
            {
                MessageBox.Show("No WiFi profiles found.", "GM - WiFi Passwords");
                return;
            }

            string result = "Saved WiFi Profiles:\n\n";
            string[] profileNames = profiles.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string name in profileNames)
            {
                try
                {
                    var psi2 = new ProcessStartInfo("netsh", "wlan show profile name=\"" + name + "\" key=clear");
                    psi2.RedirectStandardOutput = true;
                    psi2.UseShellExecute = false;
                    psi2.CreateNoWindow = true;
                    var p2 = Process.Start(psi2);
                    if (p2 == null) { result += name + " : (error)\n"; continue; }
                    string out2 = p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit();
                    p2.Dispose();

                    string key = "";
                    foreach (string l in out2.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = l.Trim();
                        if (t.StartsWith("Key Content"))
                        {
                            int c = t.IndexOf(':');
                            if (c >= 0) key = t.Substring(c + 1).Trim();
                        }
                    }
                    result += name + " : " + (key.Length > 0 ? key : "(no key)") + "\n";
                }
                catch { result += name + " : (error)\n"; }
            }

            MessageBox.Show(result.Trim(), "GM - WiFi Passwords", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("WiFi passwords displayed");
        }
        catch { MessageBox.Show("Could not retrieve WiFi passwords.", "GM"); }
    }

    static void OpenQuickNotes()
    {
        var f = new Form();
        f.Text = "GM - Quick Notes";
        f.Size = new Size(450, 380);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        string notesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gm_notes.txt");

        var txt = new TextBox();
        txt.Multiline = true;
        txt.ScrollBars = ScrollBars.Vertical;
        txt.Dock = DockStyle.Fill;
        txt.BackColor = Color.FromArgb(20, 20, 35);
        txt.ForeColor = Color.FromArgb(0, 200, 100);
        txt.Font = new Font("Consolas", 11);
        txt.BorderStyle = BorderStyle.None;

        try { if (File.Exists(notesFile)) txt.Text = File.ReadAllText(notesFile); } catch { }

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnSave = new Button { Text = "Save", Location = new Point(10, 305), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(100, 305), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(160, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "Auto-saves on close", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(200, 312) };

        btnSave.Click += (s, e) => { try { File.WriteAllText(notesFile, txt.Text); lblStatus2.Text = "Saved!"; } catch { lblStatus2.Text = "Save failed"; } };
        btnClear.Click += (s, e) => { if (MessageBox.Show("Clear all notes?", "Quick Notes", MessageBoxButtons.YesNo) == DialogResult.Yes) txt.Text = ""; };

        f.FormClosed += (s, e) => { try { File.WriteAllText(notesFile, txt.Text); } catch { } btnFont.Dispose(); lblStatus2.Font.Dispose(); txt.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { txt, btnSave, btnClear, lblStatus2 });
        f.Show();
        SetStatus("Quick Notes opened");
    }

    static void OpenFileHash()
    {
        var f = new Form();
        f.Text = "GM - File Hash";
        f.Size = new Size(500, 260);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblFile = new Label { Text = "No file selected", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(470, 20), Location = new Point(10, 10) };
        var lblMd5 = new Label { Text = "MD5: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = false, Size = new Size(470, 20), Location = new Point(10, 40) };
        var lblSha1 = new Label { Text = "SHA1: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = false, Size = new Size(470, 20), Location = new Point(10, 65) };
        var lblSha256 = new Label { Text = "SHA256: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = false, Size = new Size(470, 20), Location = new Point(10, 90) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnBrowse = new Button { Text = "Browse", Location = new Point(10, 120), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnCopyMd5 = new Button { Text = "Copy MD5", Location = new Point(110, 120), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyMd5.FlatAppearance.BorderSize = 0;
        var btnCopySha256 = new Button { Text = "Copy SHA256", Location = new Point(210, 120), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha256.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 165) };

        string lastMd5 = "", lastSha256 = "";

        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File to Hash", "Any file");
            if (path != null)
            {
                try
                {
                    lblFile.Text = Path.GetFileName(path);
                    lblStatus2.Text = "Calculating...";
                    using (var md5 = MD5.Create())
                    using (var sha1 = System.Security.Cryptography.SHA1.Create())
                    using (var sha256 = System.Security.Cryptography.SHA256.Create())
                    using (var fs = File.OpenRead(path))
                    {
                        byte[] hash;
                        hash = md5.ComputeHash(fs);
                        lastMd5 = BitConverter.ToString(hash).Replace("-", "").ToLower();
                        lblMd5.Text = "MD5:    " + lastMd5;

                        fs.Position = 0;
                        hash = sha1.ComputeHash(fs);
                        lblSha1.Text = "SHA1:   " + BitConverter.ToString(hash).Replace("-", "").ToLower();

                        fs.Position = 0;
                        hash = sha256.ComputeHash(fs);
                        lastSha256 = BitConverter.ToString(hash).Replace("-", "").ToLower();
                        lblSha256.Text = "SHA256: " + lastSha256;
                    }
                    lblStatus2.Text = "Hashes calculated";
                }
                catch { lblStatus2.Text = "Error calculating hash"; }
            }
        };
        btnCopyMd5.Click += (s, e) => { try { if (lastMd5.Length > 0) { Clipboard.SetText(lastMd5); lblStatus2.Text = "MD5 copied"; } } catch { } };
        btnCopySha256.Click += (s, e) => { try { if (lastSha256.Length > 0) { Clipboard.SetText(lastSha256); lblStatus2.Text = "SHA256 copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblFile.Font.Dispose(); lblMd5.Font.Dispose(); lblSha1.Font.Dispose(); lblSha256.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, lblMd5, lblSha1, lblSha256, btnBrowse, btnCopyMd5, btnCopySha256, lblStatus2 });
        f.Show();
        SetStatus("File Hash opened");
    }

    static void OpenBulkRenamer()
    {
        var f = new Form();
        f.Text = "GM - Bulk Renamer";
        f.Size = new Size(550, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var listFiles = new ListBox();
        listFiles.Font = new Font("Consolas", 9);
        listFiles.BackColor = Color.FromArgb(20, 20, 35);
        listFiles.ForeColor = Color.FromArgb(0, 200, 100);
        listFiles.Dock = DockStyle.Top;
        listFiles.Height = 150;
        listFiles.SelectionMode = SelectionMode.MultiExtended;

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblFind = new Label { Text = "Find:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 162) };
        var txtFind = new TextBox { Font = new Font("Consolas", 10), Size = new Size(200, 25), Location = new Point(60, 159), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblReplace = new Label { Text = "Replace:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 192) };
        var txtReplace = new TextBox { Font = new Font("Consolas", 10), Size = new Size(200, 25), Location = new Point(60, 189), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

        var chkPrefix = new CheckBox { Text = "Add prefix", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(280, 162) };
        var txtPrefix = new TextBox { Font = new Font("Consolas", 10), Size = new Size(120, 25), Location = new Point(280, 189), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var chkNumber = new CheckBox { Text = "Sequential numbering", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(280, 222) };
        var txtStartNum = new TextBox { Font = new Font("Consolas", 10), Size = new Size(50, 25), Location = new Point(280, 249), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "1" };

        var btnAddFiles = new Button { Text = "Add Files", Location = new Point(10, 285), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAddFiles.FlatAppearance.BorderSize = 0;
        var btnAddFolder = new Button { Text = "Add Folder", Location = new Point(110, 285), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAddFolder.FlatAppearance.BorderSize = 0;
        var btnClearList = new Button { Text = "Clear", Location = new Point(210, 285), Size = new Size(70, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClearList.FlatAppearance.BorderSize = 0;
        var btnPreview = new Button { Text = "Preview", Location = new Point(290, 285), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnPreview.FlatAppearance.BorderSize = 0;
        var btnRename = new Button { Text = "Rename All", Location = new Point(380, 285), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 160, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRename.FlatAppearance.BorderSize = 0;

        var lblInfo = new Label { Text = "0 files loaded", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(10, 330) };
        var lblPreview2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = false, Size = new Size(520, 40), Location = new Point(10, 350) };

        btnAddFiles.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File to Rename", "Any file");
            if (path != null)
            {
                listFiles.Items.Add(path);
                lblInfo.Text = listFiles.Items.Count + " files loaded";
            }
        };
        btnAddFolder.Click += (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder with files to rename";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    foreach (string file in Directory.GetFiles(dlg.SelectedPath)) listFiles.Items.Add(file);
                    lblInfo.Text = listFiles.Items.Count + " files loaded";
                }
            }
        };
        btnClearList.Click += (s, e) => { listFiles.Items.Clear(); lblInfo.Text = "0 files loaded"; lblPreview2.Text = ""; };

        Func<string, int, string> getNewName = (path, idx) =>
        {
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            if (txtFind.Text.Length > 0) name = name.Replace(txtFind.Text, txtReplace.Text);
            if (chkPrefix.Checked && txtPrefix.Text.Length > 0) name = txtPrefix.Text + name;
            if (chkNumber.Checked)
            {
                int startNum = 1;
                int.TryParse(txtStartNum.Text, out startNum);
                name = name + "_" + (startNum + idx).ToString("D3");
            }
            return Path.Combine(dir, name + ext);
        };

        btnPreview.Click += (s, e) =>
        {
            string preview = "";
            int count = Math.Min(listFiles.Items.Count, 5);
            for (int i = 0; i < count; i++)
            {
                string oldName = Path.GetFileName(listFiles.Items[i].ToString());
                string newName = Path.GetFileName(getNewName(listFiles.Items[i].ToString(), i));
                preview += oldName + " -> " + newName + "\n";
            }
            if (listFiles.Items.Count > 5) preview += "... and " + (listFiles.Items.Count - 5) + " more";
            lblPreview2.Text = preview.Length > 0 ? preview : "No files to rename";
        };

        btnRename.Click += (s, e) =>
        {
            if (listFiles.Items.Count == 0) { MessageBox.Show("No files loaded.", "GM"); return; }
            string msg = "Rename " + listFiles.Items.Count + " files?";
            if (MessageBox.Show(msg, "Bulk Renamer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            int success = 0, fail = 0;
            for (int i = 0; i < listFiles.Items.Count; i++)
            {
                try
                {
                    string oldPath = listFiles.Items[i].ToString();
                    string newPath = getNewName(oldPath, i);
                    if (oldPath != newPath && File.Exists(oldPath))
                    {
                        File.Move(oldPath, newPath);
                        success++;
                    }
                }
                catch { fail++; }
            }
            lblInfo.Text = "Done: " + success + " renamed, " + fail + " failed";
            MessageBox.Show("Renamed: " + success + "\nFailed: " + fail, "Bulk Renamer");
            SetStatus("Bulk rename: " + success + " files");
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); listFiles.Font.Dispose(); lblFind.Font.Dispose(); txtFind.Font.Dispose(); lblReplace.Font.Dispose(); txtReplace.Font.Dispose(); chkPrefix.Font.Dispose(); txtPrefix.Font.Dispose(); chkNumber.Font.Dispose(); txtStartNum.Font.Dispose(); lblInfo.Font.Dispose(); lblPreview2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { listFiles, lblFind, txtFind, lblReplace, txtReplace, chkPrefix, txtPrefix, chkNumber, txtStartNum, btnAddFiles, btnAddFolder, btnClearList, btnPreview, btnRename, lblInfo, lblPreview2 });
        f.Show();
        SetStatus("Bulk Renamer opened");
    }

    static void OpenBase64()
    {
        var f = new Form();
        f.Text = "GM - Base64 Encoder/Decoder";
        f.Size = new Size(450, 350);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(410, 80), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Output:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 120) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(410, 80), Location = new Point(10, 140), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnEncode = new Button { Text = "Encode", Location = new Point(10, 235), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnEncode.FlatAppearance.BorderSize = 0;
        var btnDecode = new Button { Text = "Decode", Location = new Point(120, 235), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDecode.FlatAppearance.BorderSize = 0;
        var btnCopyOut = new Button { Text = "Copy Output", Location = new Point(230, 235), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyOut.FlatAppearance.BorderSize = 0;
        var btnClear2 = new Button { Text = "Clear", Location = new Point(340, 235), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear2.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 280) };

        btnEncode.Click += (s, e) => { try { txtOutput.Text = Convert.ToBase64String(Encoding.UTF8.GetBytes(txtInput.Text)); lblStatus2.Text = "Encoded"; } catch { lblStatus2.Text = "Encode error"; } };
        btnDecode.Click += (s, e) => { try { txtOutput.Text = Encoding.UTF8.GetString(Convert.FromBase64String(txtInput.Text)); lblStatus2.Text = "Decoded"; } catch { lblStatus2.Text = "Decode error - invalid Base64"; } };
        btnCopyOut.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };
        btnClear2.Click += (s, e) => { txtInput.Text = ""; txtOutput.Text = ""; lblStatus2.Text = ""; };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnEncode, btnDecode, btnCopyOut, btnClear2, lblStatus2 });
        f.Show();
        SetStatus("Base64 opened");
    }

    static void OpenPasswordGen()
    {
        var f = new Form();
        f.Text = "GM - Password Generator";
        f.Size = new Size(380, 320);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblLen = new Label { Text = "Length:", Font = new Font("Segoe UI", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 20) };
        var txtLen = new TextBox { Font = new Font("Consolas", 12), Size = new Size(60, 28), Location = new Point(90, 17), Text = "16", TextAlign = HorizontalAlignment.Center, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var chkUpper = new CheckBox { Text = "Uppercase (A-Z)", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 55), Checked = true };
        var chkLower = new CheckBox { Text = "Lowercase (a-z)", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 80), Checked = true };
        var chkDigits = new CheckBox { Text = "Digits (0-9)", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 105), Checked = true };
        var chkSymbols = new CheckBox { Text = "Symbols (!@#$...)", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 130), Checked = true };

        var txtResult = new TextBox { Font = new Font("Consolas", 12), Size = new Size(330, 30), Location = new Point(10, 170), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, TextAlign = HorizontalAlignment.Center };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGen = new Button { Text = "Generate", Location = new Point(10, 215), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGen.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(120, 215), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var btnClear2 = new Button { Text = "Clear", Location = new Point(210, 215), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear2.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 260) };

        btnGen.Click += (s, e) =>
        {
            int len; if (!int.TryParse(txtLen.Text, out len) || len < 4 || len > 128) { lblStatus2.Text = "Enter 4-128"; return; }
            string chars = "";
            if (chkUpper.Checked) chars += "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (chkLower.Checked) chars += "abcdefghijklmnopqrstuvwxyz";
            if (chkDigits.Checked) chars += "0123456789";
            if (chkSymbols.Checked) chars += "!@#$%^&*()_+-=[]{}|;:,.<>?";
            if (chars.Length == 0) { lblStatus2.Text = "Select at least one"; return; }
            var rngBytes = new byte[len];
            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) { rng.GetBytes(rngBytes); }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(chars[rngBytes[i] % chars.Length]);
            txtResult.Text = sb.ToString();
            lblStatus2.Text = "Generated " + len + " char password";
        };
        btnCopy.Click += (s, e) => { try { if (txtResult.Text.Length > 0) { Clipboard.SetText(txtResult.Text); lblStatus2.Text = "Copied"; } } catch { } };
        btnClear2.Click += (s, e) => { txtResult.Text = ""; lblStatus2.Text = ""; };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblLen.Font.Dispose(); txtLen.Font.Dispose(); chkUpper.Font.Dispose(); chkLower.Font.Dispose(); chkDigits.Font.Dispose(); chkSymbols.Font.Dispose(); txtResult.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblLen, txtLen, chkUpper, chkLower, chkDigits, chkSymbols, txtResult, btnGen, btnCopy, btnClear2, lblStatus2 });
        f.Show();
        SetStatus("Password Generator opened");
    }

    static void OpenProcessPriority()
    {
        var f = new Form();
        f.Text = "GM - Process Priority";
        f.Size = new Size(500, 450);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var searchBox = new TextBox { Font = new Font("Segoe UI", 9), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, Dock = DockStyle.Top, Height = 28 };
        var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 90, BackColor = Color.FromArgb(20, 20, 30) };
        var lblInfo = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = true, Location = new Point(10, 65) };
        var btnRefresh = new Button { Text = "Refresh", Location = new Point(10, 8), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRefresh.FlatAppearance.BorderSize = 0;
        var btnLow = new Button { Text = "Low", Location = new Point(90, 8), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 160, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLow.FlatAppearance.BorderSize = 0;
        var btnNormal = new Button { Text = "Normal", Location = new Point(160, 8), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnNormal.FlatAppearance.BorderSize = 0;
        var btnHigh = new Button { Text = "High", Location = new Point(240, 8), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 120, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnHigh.FlatAppearance.BorderSize = 0;
        var btnReal = new Button { Text = "Realtime", Location = new Point(310, 8), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnReal.FlatAppearance.BorderSize = 0;

        Action refreshList = () =>
        {
            list.Items.Clear();
            string filter = searchBox.Text.Trim().ToLower();
            var procs = Process.GetProcesses();
            var sorted = new List<Process>();
            foreach (var p in procs) { try { sorted.Add(p); } catch { try { p.Dispose(); } catch { } } }
            sorted.Sort((a, b) => { try { return string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase); } catch { return 0; } });
            int shown = 0;
            foreach (var p in sorted)
            {
                try
                {
                    if (filter.Length > 0 && !p.ProcessName.ToLower().Contains(filter) && !p.Id.ToString().Contains(filter)) continue;
                    string pri = "";
                    try { pri = p.PriorityClass.ToString(); } catch { pri = "?"; }
                    string entry = String.Format("{0,-28} PID:{1,-8} {2}", p.ProcessName.Length > 26 ? p.ProcessName.Substring(0, 23) + "..." : p.ProcessName, p.Id, pri);
                    list.Items.Add(entry);
                    shown++;
                }
                catch { }
            }
            foreach (var p in procs) { try { p.Dispose(); } catch { } }
            lblInfo.Text = shown + " processes shown";
        };

        Action<string> setPriority = (level) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedItem == null) { MessageBox.Show("Select a process first.", "GM"); return; }
            string selected = list.SelectedItem.ToString();
            int pidStart = selected.IndexOf("PID:");
            int pidEnd = selected.IndexOf(" ", pidStart + 4);
            if (pidStart < 0 || pidEnd < 0) return;
            int pid;
            if (!int.TryParse(selected.Substring(pidStart + 4, pidEnd - pidStart - 4), out pid)) return;
            try
            {
                var proc = Process.GetProcessById(pid);
                try
                {
                    ProcessPriorityClass pc;
                    switch (level)
                    {
                        case "Low": pc = ProcessPriorityClass.BelowNormal; break;
                        case "High": pc = ProcessPriorityClass.AboveNormal; break;
                        case "Realtime": pc = ProcessPriorityClass.RealTime; break;
                        default: pc = ProcessPriorityClass.Normal; break;
                    }
                    proc.PriorityClass = pc;
                    lblInfo.Text = "Set " + proc.ProcessName + " to " + pc;
                    SetStatus("Priority: " + proc.ProcessName + " -> " + pc);
                    refreshList();
                }
                catch { lblInfo.Text = "Cannot set priority (access denied?)"; }
                finally { try { proc.Dispose(); } catch { } }
            }
            catch { lblInfo.Text = "Process not found"; }
        };

        searchBox.TextChanged += (s2, e2) => refreshList();
        btnRefresh.Click += (s2, e2) => refreshList();
        btnLow.Click += (s2, e2) => setPriority("Low");
        btnNormal.Click += (s2, e2) => setPriority("Normal");
        btnHigh.Click += (s2, e2) => setPriority("High");
        btnReal.Click += (s2, e2) => setPriority("Realtime");

        panel.Controls.AddRange(new Control[] { btnRefresh, btnLow, btnNormal, btnHigh, btnReal, lblInfo });
        f.Controls.Add(list);
        f.Controls.Add(searchBox);
        f.Controls.Add(panel);
        refreshList();
        f.FormClosed += (s, e) => { searchBox.Font.Dispose(); list.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Process Priority opened");
    }

    static void OpenStartupManager()
    {
        var f = new Form();
        f.Text = "GM - Startup Manager";
        f.Size = new Size(500, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        var lblInfo = new Label { Text = "Startup programs (Current User):", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(10, 10) };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 320) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnRefresh = new Button { Text = "Refresh", Location = new Point(10, 345), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRefresh.FlatAppearance.BorderSize = 0;
        var btnDisable = new Button { Text = "Disable Selected", Location = new Point(100, 345), Size = new Size(110, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDisable.FlatAppearance.BorderSize = 0;
        var btnExplorer = new Button { Text = "Open Folder", Location = new Point(220, 345), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnExplorer.FlatAppearance.BorderSize = 0;

        Action refreshStartup = null;
        refreshStartup = () =>
        {
            list.Items.Clear();
            string startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            try
            {
                foreach (string file in Directory.GetFiles(startupPath))
                {
                    string name = Path.GetFileName(file);
                    string ext = Path.GetExtension(file).ToLower();
                    string type = ext == ".lnk" ? "[Shortcut]" : ext == ".exe" ? "[Program]" : "[File]";
                    list.Items.Add(type + " " + name + "  (" + file + ")");
                }
            }
            catch { lblStatus2.Text = "Cannot read startup folder"; }
            string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath))
                {
                    if (k != null)
                    {
                        foreach (string name in k.GetValueNames())
                        {
                            string val = k.GetValue(name) != null ? k.GetValue(name).ToString() : "";
                            list.Items.Add("[Registry] " + name + "  (" + val + ")");
                        }
                    }
                }
            }
            catch { }
            lblInfo.Text = "Startup items: " + list.Items.Count;
        };

        btnRefresh.Click += (s, e) => refreshStartup();
        btnDisable.Click += (s, e) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedItem == null) { MessageBox.Show("Select an item first.", "GM"); return; }
            string selected = list.SelectedItem.ToString();
            if (selected.Contains("[Registry]"))
            {
                int nameStart = selected.IndexOf("] ") + 2;
                int nameEnd = selected.IndexOf("  (", nameStart);
                if (nameStart < 2 || nameEnd < 0) return;
                string name = selected.Substring(nameStart, nameEnd - nameStart);
                if (MessageBox.Show("Remove from registry startup?\n" + name, "Startup Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (k != null) k.DeleteValue(name, false);
                    }
                    lblStatus2.Text = "Removed: " + name;
                    refreshStartup();
                }
                catch { lblStatus2.Text = "Failed to remove from registry"; }
            }
            else if (selected.Contains("[Shortcut]") || selected.Contains("[File]") || selected.Contains("[Program]"))
            {
                int pathStart = selected.LastIndexOf("(") + 1;
                int pathEnd = selected.LastIndexOf(")");
                if (pathStart < 1 || pathEnd < 0) return;
                string filePath = selected.Substring(pathStart, pathEnd - pathStart);
                if (MessageBox.Show("Delete this startup file?\n" + filePath, "Startup Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    File.Delete(filePath);
                    lblStatus2.Text = "Deleted: " + Path.GetFileName(filePath);
                    refreshStartup();
                }
                catch { lblStatus2.Text = "Failed to delete file"; }
            }
        };
        btnExplorer.Click += (s, e) =>
        {
            try { Process.Start("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup))); }
            catch { }
        };

        refreshStartup();
        f.Controls.Add(list);
        f.Controls.Add(lblInfo);
        f.Controls.Add(lblStatus2);
        f.Controls.Add(btnRefresh);
        f.Controls.Add(btnDisable);
        f.Controls.Add(btnExplorer);
        f.FormClosed += (fs, fe) => { btnFont.Dispose(); list.Font.Dispose(); lblInfo.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Startup Manager opened");
    }

    static void OpenQuickClean()
    {
        if (MessageBox.Show("Quick Clean will remove:\n- Temp files\n- Thumbnail cache\n- Recycle Bin\n\nContinue?", "GM - Quick Clean", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int total = 0;
        try
        {
            string temp = Path.GetTempPath();
            foreach (string f in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(f); total++; } catch { }
            }
            var dirs = Directory.GetDirectories(temp, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToArray();
            foreach (string d in dirs)
            {
                try { Directory.Delete(d, false); total++; } catch { }
            }
        }
        catch { }
        try
        {
            string thumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            if (Directory.Exists(thumbPath))
            {
                foreach (string f in Directory.GetFiles(thumbPath, "thumbcache_*.db"))
                {
                    try { File.Delete(f); total++; } catch { }
                }
            }
        }
        catch { }
        try { SHEmptyRecycleBin(IntPtr.Zero, null, 7); } catch { }
        MessageBox.Show("Quick Clean done.\n" + total + " temp items removed + recycle bin emptied.", "GM - Quick Clean");
        SetStatus("Quick Clean: " + total + " items");
    }

    static void ShowPublicIP()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell", "-Command \"(Invoke-WebRequest -Uri 'https://api.ipify.org' -UseBasicParsing).Content\"");
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            var p = Process.Start(psi);
            if (p == null) { MessageBox.Show("Could not run PowerShell.", "GM"); return; }
            string ip = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            p.Dispose();

            string info = "Public IP: " + ip + "\n\n";
            info += "Check your IP info at:\nhttps://ipinfo.io/" + ip;
            MessageBox.Show(info, "GM - Public IP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("Public IP: " + ip);
        }
        catch { MessageBox.Show("Could not retrieve public IP.", "GM"); }
    }

    static string PromptFilePath(string title, string filter)
    {
        using (var f = new Form())
        {
            f.Text = title;
            f.Size = new Size(480, 130);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var lbl = new Label { Text = "Paste or type file path:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
            var txt = new TextBox { Font = new Font("Consolas", 10), Size = new Size(340, 24), Location = new Point(10, 38), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var btnOk = new Button { Text = "OK", Location = new Point(360, 36), Size = new Size(50, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button { Text = "Cancel", Location = new Point(360, 68), Size = new Size(50, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;
            var lblFilter = new Label { Text = filter, Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 66) };
            string result = null;
            btnOk.Click += (s, e) => { if (txt.Text.Trim().Length > 0 && File.Exists(txt.Text.Trim())) { result = txt.Text.Trim(); f.Close(); } else { lblFilter.Text = "File not found - check the path"; lblFilter.ForeColor = Color.FromArgb(180, 60, 60); } };
            btnCancel.Click += (s, e) => { f.Close(); };
            txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { btnOk.PerformClick(); e.SuppressKeyPress = true; } };
            f.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel, lblFilter });
            f.ActiveControl = txt;
            f.ShowDialog();
            lblFont.Dispose();
            btnFont.Dispose();
            ico.Dispose();
            return result;
        }
    }

    static string PromptSavePath(string title, string filter, string defaultName)
    {
        using (var f = new Form())
        {
            f.Text = title;
            f.Size = new Size(480, 130);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var lbl = new Label { Text = "Enter save path:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
            var txt = new TextBox { Font = new Font("Consolas", 10), Size = new Size(340, 24), Location = new Point(10, 38), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = defaultName };
            var btnOk = new Button { Text = "Save", Location = new Point(360, 36), Size = new Size(50, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button { Text = "Cancel", Location = new Point(360, 68), Size = new Size(50, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;
            var lblFilter = new Label { Text = filter, Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 66) };
            string result = null;
            btnOk.Click += (s, e) => { if (txt.Text.Trim().Length > 0) { result = txt.Text.Trim(); f.Close(); } };
            btnCancel.Click += (s, e) => { f.Close(); };
            txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { btnOk.PerformClick(); e.SuppressKeyPress = true; } };
            f.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel, lblFilter });
            f.ActiveControl = txt;
            f.ShowDialog();
            lblFont.Dispose();
            btnFont.Dispose();
            ico.Dispose();
            return result;
        }
    }

    static void OpenFileInfo()
    {
        var f = new Form();
        f.Text = "GM - File Info";
        f.Size = new Size(450, 350);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblFile = new Label { Text = "No file selected", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(410, 20), Location = new Point(10, 10) };
        var txtInfo = new TextBox { Multiline = true, ReadOnly = true, Font = new Font("Consolas", 10), Size = new Size(410, 200), Location = new Point(10, 40), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnBrowse = new Button { Text = "Browse", Location = new Point(10, 255), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(110, 255), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(200, 262) };

        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File", "Any file");
            if (path != null)
            {
                try
                {
                    var fi = new FileInfo(path);
                    string info = "";
                    info += "Name: " + fi.Name + "\n";
                    info += "Full Path: " + fi.FullName + "\n";
                    info += "Directory: " + fi.DirectoryName + "\n";
                    info += "Extension: " + fi.Extension + "\n\n";
                    info += "Size: " + fi.Length + " bytes";
                    if (fi.Length > 1048576) info += " (" + (fi.Length / 1048576) + " MB)";
                    else if (fi.Length > 1024) info += " (" + (fi.Length / 1024) + " KB)";
                    info += "\n";
                    info += "Created: " + fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") + "\n";
                    info += "Modified: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "\n";
                    info += "Accessed: " + fi.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n";
                    info += "Read Only: " + fi.IsReadOnly + "\n";
                    info += "Hidden: " + ((fi.Attributes & FileAttributes.Hidden) != 0) + "\n";
                    info += "System: " + ((fi.Attributes & FileAttributes.System) != 0) + "\n";
                    info += "Attributes: " + fi.Attributes;
                    lblFile.Text = fi.Name;
                    txtInfo.Text = info;
                    lblStatus2.Text = "File info loaded";
                }
                catch { lblStatus2.Text = "Error reading file info"; }
            }
        };
        btnCopy.Click += (s, e) => { try { if (txtInfo.Text.Length > 0) { Clipboard.SetText(txtInfo.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblFile.Font.Dispose(); txtInfo.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, txtInfo, btnBrowse, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("File Info opened");
    }

    static void OpenScreenshotTimer()
    {
        var f = new Form();
        f.Text = "GM - Screenshot Timer";
        f.Size = new Size(320, 220);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblTitle = new Label { Text = "Timed Screenshot", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(70, 10) };
        var lblSec = new Label { Text = "Seconds:", Font = new Font("Segoe UI", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(20, 50) };
        var txtSec = new TextBox { Font = new Font("Consolas", 14), Size = new Size(60, 28), Location = new Point(100, 47), Text = "5", TextAlign = HorizontalAlignment.Center, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblCountdown = new Label { Text = "", Font = new Font("Consolas", 24, FontStyle.Bold), ForeColor = Color.Lime, AutoSize = false, Size = new Size(280, 40), Location = new Point(10, 85), TextAlign = ContentAlignment.MiddleCenter };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnStart = new Button { Text = "Start", Location = new Point(20, 135), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStart.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button { Text = "Cancel", Location = new Point(110, 135), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Enabled = false;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(200, 142) };

        var ticker = new System.Windows.Forms.Timer { Interval = 1000 };
        int secondsLeft = 0;
        ticker.Tick += (s, e) =>
        {
            secondsLeft--;
            if (secondsLeft <= 0) { ticker.Stop(); lblCountdown.Text = ""; Screenshot(); lblStatus2.Text = "Screenshot taken!"; btnStart.Enabled = true; btnCancel.Enabled = false; txtSec.Enabled = true; return; }
            lblCountdown.Text = secondsLeft.ToString();
        };
        btnStart.Click += (s, e) =>
        {
            int sec; if (!int.TryParse(txtSec.Text, out sec) || sec < 1 || sec > 60) { lblStatus2.Text = "Enter 1-60"; return; }
            secondsLeft = sec; lblCountdown.Text = secondsLeft.ToString(); ticker.Start();
            btnStart.Enabled = false; btnCancel.Enabled = true; txtSec.Enabled = false;
        };
        btnCancel.Click += (s, e) => { ticker.Stop(); lblCountdown.Text = ""; lblStatus2.Text = "Cancelled"; btnStart.Enabled = true; btnCancel.Enabled = false; txtSec.Enabled = true; };

        f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); btnFont.Dispose(); lblTitle.Font.Dispose(); lblSec.Font.Dispose(); txtSec.Font.Dispose(); lblCountdown.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblTitle, lblSec, txtSec, lblCountdown, btnStart, btnCancel, lblStatus2 });
        f.Show();
        SetStatus("Screenshot Timer opened");
    }

    static void OpenTextTools()
    {
        var f = new Form();
        f.Text = "GM - Text Tools";
        f.Size = new Size(450, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(410, 100), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Output:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 140) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(410, 80), Location = new Point(10, 160), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        int bx = 10, by = 255;
        var btnUpper = new Button { Text = "UPPER", Location = new Point(bx, by), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnUpper.FlatAppearance.BorderSize = 0;
        var btnLower = new Button { Text = "lower", Location = new Point(bx + 65, by), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLower.FlatAppearance.BorderSize = 0;
        var btnTitle = new Button { Text = "Title", Location = new Point(bx + 130, by), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnTitle.FlatAppearance.BorderSize = 0;
        var btnReverse = new Button { Text = "Reverse", Location = new Point(bx + 195, by), Size = new Size(65, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnReverse.FlatAppearance.BorderSize = 0;
        var btnWords = new Button { Text = "Word Count", Location = new Point(bx + 265, by), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnWords.FlatAppearance.BorderSize = 0;
        var btnTrim = new Button { Text = "Trim", Location = new Point(bx + 350, by), Size = new Size(55, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnTrim.FlatAppearance.BorderSize = 0;
        var btnCopyOut = new Button { Text = "Copy Output", Location = new Point(bx, by + 35), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyOut.FlatAppearance.BorderSize = 0;
        var lblInfo = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(100, by + 42) };

        btnUpper.Click += (s, e) => { txtOutput.Text = txtInput.Text.ToUpper(); };
        btnLower.Click += (s, e) => { txtOutput.Text = txtInput.Text.ToLower(); };
        btnTitle.Click += (s, e) =>
        {
            string src = txtInput.Text;
            StringBuilder sb = new StringBuilder();
            bool newWord = true;
            foreach (char c in src)
            {
                if (char.IsLetter(c)) { sb.Append(newWord ? char.ToUpper(c) : char.ToLower(c)); newWord = false; }
                else { sb.Append(c); newWord = true; }
            }
            txtOutput.Text = sb.ToString();
        };
        btnReverse.Click += (s, e) =>
        {
            char[] arr = txtInput.Text.ToCharArray();
            Array.Reverse(arr);
            txtOutput.Text = new string(arr);
        };
        btnWords.Click += (s, e) =>
        {
            string text = txtInput.Text.Trim();
            int words = text.Length == 0 ? 0 : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int lines = text.Length == 0 ? 0 : text.Split('\n').Length;
            int chars = text.Length;
            lblInfo.Text = "Words: " + words + " | Lines: " + lines + " | Chars: " + chars;
        };
        btnTrim.Click += (s, e) => { txtOutput.Text = txtInput.Text.Trim(); };
        btnCopyOut.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblInfo.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnUpper, btnLower, btnTitle, btnReverse, btnWords, btnTrim, btnCopyOut, lblInfo });
        f.Show();
        SetStatus("Text Tools opened");
    }

    static void OpenColorPalette()
    {
        var f = new Form();
        f.Text = "GM - Color Palette";
        f.Size = new Size(450, 350);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Color[] palette = {
            Color.FromArgb(255, 0, 0), Color.FromArgb(255, 87, 51), Color.FromArgb(255, 165, 0),
            Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 128, 0),
            Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(75, 0, 130),
            Color.FromArgb(148, 0, 211), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 192, 203),
            Color.FromArgb(128, 0, 0), Color.FromArgb(128, 128, 0), Color.FromArgb(0, 128, 128),
            Color.FromArgb(0, 0, 128), Color.FromArgb(128, 0, 128), Color.FromArgb(192, 192, 192),
            Color.FromArgb(128, 128, 128), Color.FromArgb(64, 64, 64), Color.FromArgb(255, 250, 240),
            Color.FromArgb(245, 245, 220), Color.FromArgb(0, 0, 0)
        };

        var panel = new Panel { AutoScroll = true, Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 35) };
        var lblSelected = new Label { Text = "Click a color to copy its hex value", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleCenter };

        int cx = 10, cy = 10;
        foreach (Color c in palette)
        {
            var swatch = new Label
            {
                Size = new Size(50, 40),
                Location = new Point(cx, cy),
                BackColor = c,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            swatch.Tag = hex;
            swatch.Click += (s, e) =>
            {
                try { Clipboard.SetText((string)swatch.Tag); lblSelected.Text = "Copied: " + (string)swatch.Tag; } catch { }
            };
            panel.Controls.Add(swatch);
            cx += 58;
            if (cx + 50 > 420) { cx = 10; cy += 48; }
        }

        f.Controls.Add(panel);
        f.Controls.Add(lblSelected);
        f.FormClosed += (s, e) => { ico.Dispose(); lblSelected.Font.Dispose(); };
        f.Show();
        SetStatus("Color Palette opened");
    }

    static void OpenCalculator()
    {
        var f = new Form();
        f.Text = "GM - Calculator";
        f.Size = new Size(300, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var txtDisplay = new TextBox { Font = new Font("Consolas", 20), Size = new Size(265, 40), Location = new Point(10, 10), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right, ReadOnly = true, Text = "0" };
        Font btnFont = new Font("Consolas", 14, FontStyle.Bold);
        string[] keys = { "C", "CE", "Ã¢Å’Â«", "ÃƒÂ·", "7", "8", "9", "Ãƒâ€”", "4", "5", "6", "Ã¢Ë†â€™", "1", "2", "3", "+", "Ã‚Â±", "0", ".", "=" };
        double? acc = null;
        string op = "";
        bool resetNext = false;

        Action<string> pressKey = null;
        pressKey = (key) =>
        {
            if (key == "C") { txtDisplay.Text = "0"; acc = null; op = ""; resetNext = false; return; }
            if (key == "CE") { txtDisplay.Text = "0"; return; }
            if (key == "Ã¢Å’Â«") { if (txtDisplay.Text.Length > 1) txtDisplay.Text = txtDisplay.Text.Substring(0, txtDisplay.Text.Length - 1); else txtDisplay.Text = "0"; return; }
            if (key == "Ã‚Â±") { if (txtDisplay.Text.StartsWith("-")) txtDisplay.Text = txtDisplay.Text.Substring(1); else if (txtDisplay.Text != "0") txtDisplay.Text = "-" + txtDisplay.Text; return; }
            if ("0123456789.".Contains(key))
            {
                if (resetNext) { txtDisplay.Text = key == "." ? "0." : key; resetNext = false; }
                else if (key == "." && txtDisplay.Text.Contains(".")) return;
                else txtDisplay.Text = txtDisplay.Text == "0" && key != "." ? key : txtDisplay.Text + key;
                return;
            }
            if (key == "+" || key == "Ã¢Ë†â€™" || key == "Ãƒâ€”" || key == "ÃƒÂ·")
            {
                double val; if (!double.TryParse(txtDisplay.Text, out val)) return;
                if (acc.HasValue && op.Length > 0 && !resetNext)
                {
                    double a = acc.Value;
                    if (op == "+") val = a + val;
                    else if (op == "Ã¢Ë†â€™") val = a - val;
                    else if (op == "Ãƒâ€”") val = a * val;
                    else if (op == "ÃƒÂ·") val = val != 0 ? a / val : double.NaN;
                    txtDisplay.Text = val.ToString("G");
                }
                acc = val; op = key; resetNext = true;
                return;
            }
            if (key == "=")
            {
                double val; if (!double.TryParse(txtDisplay.Text, out val) || !acc.HasValue || op.Length == 0) return;
                double a = acc.Value;
                if (op == "+") val = a + val;
                else if (op == "Ã¢Ë†â€™") val = a - val;
                else if (op == "Ãƒâ€”") val = a * val;
                else if (op == "ÃƒÂ·") val = val != 0 ? a / val : double.NaN;
                txtDisplay.Text = val.ToString("G");
                acc = null; op = ""; resetNext = true;
            }
        };

        f.Controls.Add(txtDisplay);
        int bx = 10, by = 60;
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            Color bg = Color.FromArgb(40, 40, 55);
            if ("ÃƒÂ·Ãƒâ€”Ã¢Ë†â€™+=".Contains(k) && k.Length == 1) bg = Color.FromArgb(0, 100, 160);
            if (k == "C" || k == "CE" || k == "Ã¢Å’Â«") bg = Color.FromArgb(120, 40, 40);
            var btn = new Button { Text = k, Location = new Point(bx, by), Size = new Size(62, 42), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            string keyVal = k;
            btn.Click += (s, e) => pressKey(keyVal);
            f.Controls.Add(btn);
            bx += 67;
            if (bx > 250) { bx = 10; by += 48; }
        }

        f.KeyPreview = true;
        f.KeyDown += (s, e) =>
        {
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) pressKey(((int)(e.KeyCode - Keys.D0)).ToString());
            if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) pressKey(((int)(e.KeyCode - Keys.NumPad0)).ToString());
            if (e.KeyCode == Keys.Add) pressKey("+");
            if (e.KeyCode == Keys.Subtract) pressKey("Ã¢Ë†â€™");
            if (e.KeyCode == Keys.Multiply) pressKey("Ãƒâ€”");
            if (e.KeyCode == Keys.Divide) pressKey("ÃƒÂ·");
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return) pressKey("=");
            if (e.KeyCode == Keys.Escape) pressKey("C");
            if (e.KeyCode == Keys.Back) pressKey("Ã¢Å’Â«");
            if (e.KeyCode == Keys.Decimal) pressKey(".");
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); txtDisplay.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Calculator opened");
    }

    static void OpenUrlEncoder()
    {
        var f = new Form();
        f.Text = "GM - URL Encoder/Decoder";
        f.Size = new Size(480, 340);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, Font = new Font("Consolas", 10), Size = new Size(435, 70), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Output:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 110) };
        var txtOutput = new TextBox { Multiline = true, Font = new Font("Consolas", 10), Size = new Size(435, 70), Location = new Point(10, 130), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 210) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnEncode = new Button { Text = "URL Encode", Location = new Point(10, 240), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnEncode.FlatAppearance.BorderSize = 0;
        var btnDecode = new Button { Text = "URL Decode", Location = new Point(120, 240), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDecode.FlatAppearance.BorderSize = 0;
        var btnHtmlEnc = new Button { Text = "HTML Encode", Location = new Point(230, 240), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnHtmlEnc.FlatAppearance.BorderSize = 0;
        var btnHtmlDec = new Button { Text = "HTML Decode", Location = new Point(340, 240), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnHtmlDec.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy Output", Location = new Point(10, 280), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        btnEncode.Click += (s, e) => { try { txtOutput.Text = Uri.EscapeDataString(txtInput.Text); lblStatus2.Text = "URL Encoded"; } catch { lblStatus2.Text = "Encode error"; } };
        btnDecode.Click += (s, e) => { try { txtOutput.Text = Uri.UnescapeDataString(txtInput.Text); lblStatus2.Text = "URL Decoded"; } catch { lblStatus2.Text = "Decode error"; } };
        btnHtmlEnc.Click += (s, e) => { txtOutput.Text = System.Net.WebUtility.HtmlEncode(txtInput.Text); lblStatus2.Text = "HTML Encoded"; };
        btnHtmlDec.Click += (s, e) => { txtOutput.Text = System.Net.WebUtility.HtmlDecode(txtInput.Text); lblStatus2.Text = "HTML Decoded"; };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnEncode, btnDecode, btnHtmlEnc, btnHtmlDec, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("URL Encoder opened");
    }

    static void OpenJsonFormatter()
    {
        var f = new Form();
        f.Text = "GM - JSON Formatter";
        f.Size = new Size(500, 450);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input (JSON):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(460, 130), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Formatted:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 170) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(460, 180), Location = new Point(10, 190), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 380) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnFormat = new Button { Text = "Format", Location = new Point(10, 400), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnFormat.FlatAppearance.BorderSize = 0;
        var btnMinify = new Button { Text = "Minify", Location = new Point(100, 400), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnMinify.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(190, 400), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        btnFormat.Click += (s, e) =>
        {
            try
            {
                string input = txtInput.Text.Trim();
                int depth = 0; bool inStr = false; bool escaped = false;
                StringBuilder sb = new StringBuilder();
                foreach (char c in input)
                {
                    if (escaped) { sb.Append(c); escaped = false; continue; }
                    if (c == '\\' && inStr) { sb.Append(c); escaped = true; continue; }
                    if (c == '"') { inStr = !inStr; sb.Append(c); continue; }
                    if (inStr) { sb.Append(c); continue; }
                    if (c == '{' || c == '[') { sb.Append(c); sb.Append('\n'); depth++; for (int i = 0; i < depth; i++) sb.Append("  "); continue; }
                    if (c == '}' || c == ']') { sb.Append('\n'); depth--; for (int i = 0; i < depth; i++) sb.Append("  "); sb.Append(c); continue; }
                    if (c == ',') { sb.Append(c); sb.Append('\n'); for (int i = 0; i < depth; i++) sb.Append("  "); continue; }
                    if (c == ':') { sb.Append(": "); continue; }
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') continue;
                    sb.Append(c);
                }
                txtOutput.Text = sb.ToString();
                lblStatus2.Text = "Formatted (" + input.Length + " chars)";
            }
            catch { lblStatus2.Text = "Format error"; }
        };
        btnMinify.Click += (s, e) =>
        {
            try
            {
                string input = txtInput.Text.Trim();
                bool inStr = false; bool escaped = false; bool lastWasSpace = false;
                StringBuilder sb = new StringBuilder();
                foreach (char c in input)
                {
                    if (escaped) { sb.Append(c); escaped = false; lastWasSpace = false; continue; }
                    if (c == '\\' && inStr) { sb.Append(c); escaped = true; lastWasSpace = false; continue; }
                    if (c == '"') { inStr = !inStr; sb.Append(c); lastWasSpace = false; continue; }
                    if (inStr) { sb.Append(c); lastWasSpace = false; continue; }
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { if (!lastWasSpace) lastWasSpace = true; continue; }
                    sb.Append(c); lastWasSpace = false;
                }
                txtOutput.Text = sb.ToString();
                lblStatus2.Text = "Minified (" + sb.Length + " chars)";
            }
            catch { lblStatus2.Text = "Minify error"; }
        };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnFormat, btnMinify, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("JSON Formatter opened");
    }

    static void OpenRegexTester()
    {
        var f = new Form();
        f.Text = "GM - Regex Tester";
        f.Size = new Size(500, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblPattern = new Label { Text = "Pattern:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtPattern = new TextBox { Font = new Font("Consolas", 11), Size = new Size(460, 28), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle };
        var lblInput = new Label { Text = "Test string:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 65) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(460, 100), Location = new Point(10, 85), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblResult = new Label { Text = "", Font = new Font("Consolas", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(460, 100), Location = new Point(10, 195) };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 360) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnTest = new Button { Text = "Test", Location = new Point(10, 310), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnTest.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(100, 310), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy Matches", Location = new Point(190, 310), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        btnTest.Click += (s, e) =>
        {
            try
            {
                var rx = new System.Text.RegularExpressions.Regex(txtPattern.Text);
                var matches = rx.Matches(txtInput.Text);
                if (matches.Count == 0) { lblResult.Text = "No matches found."; lblStatus2.Text = "0 matches"; return; }
                string result = "Matches: " + matches.Count + "\n\n";
                for (int i = 0; i < Math.Min(matches.Count, 30); i++)
                {
                    result += "[" + i + "] \"" + matches[i].Value + "\" at index " + matches[i].Index + "\n";
                    for (int g = 1; g < matches[i].Groups.Count; g++)
                    {
                        if (matches[i].Groups[g].Success)
                            result += "    Group " + g + ": \"" + matches[i].Groups[g].Value + "\"\n";
                    }
                }
                if (matches.Count > 30) result += "\n... and " + (matches.Count - 30) + " more";
                lblResult.Text = result;
                lblStatus2.Text = matches.Count + " matches found";
            }
            catch (Exception ex) { lblResult.Text = "Error: " + ex.Message; lblStatus2.Text = "Invalid regex"; }
        };
        btnClear.Click += (s, e) => { txtPattern.Text = ""; txtInput.Text = ""; lblResult.Text = ""; lblStatus2.Text = ""; };
        btnCopy.Click += (s, e) => { try { if (lblResult.Text.Length > 0) { Clipboard.SetText(lblResult.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblPattern.Font.Dispose(); txtPattern.Font.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblResult.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblPattern, txtPattern, lblInput, txtInput, lblResult, btnTest, btnClear, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("Regex Tester opened");
    }

    static void OpenTextHash()
    {
        var f = new Form();
        f.Text = "GM - Text Hash";
        f.Size = new Size(480, 320);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input text:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(435, 60), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblMd5 = new Label { Text = "MD5:    -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 105) };
        var lblSha1 = new Label { Text = "SHA1:   -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 130) };
        var lblSha256 = new Label { Text = "SHA256: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 155) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnHash = new Button { Text = "Calculate", Location = new Point(10, 195), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnHash.FlatAppearance.BorderSize = 0;
        var btnCopyMd5 = new Button { Text = "Copy MD5", Location = new Point(110, 195), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyMd5.FlatAppearance.BorderSize = 0;
        var btnCopySha256 = new Button { Text = "Copy SHA256", Location = new Point(200, 195), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha256.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 240) };

        string lastMd5 = "", lastSha256 = "";

        btnHash.Click += (s, e) =>
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(txtInput.Text);
                using (var md5 = MD5.Create()) { lastMd5 = BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "").ToLower(); lblMd5.Text = "MD5:    " + lastMd5; }
                using (var sha1 = System.Security.Cryptography.SHA1.Create()) { lblSha1.Text = "SHA1:   " + BitConverter.ToString(sha1.ComputeHash(data)).Replace("-", "").ToLower(); }
                using (var sha256 = System.Security.Cryptography.SHA256.Create()) { lastSha256 = BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLower(); lblSha256.Text = "SHA256: " + lastSha256; }
                lblStatus2.Text = "Hashes calculated for " + data.Length + " bytes";
            }
            catch { lblStatus2.Text = "Error calculating hashes"; }
        };
        btnCopyMd5.Click += (s, e) => { try { if (lastMd5.Length > 0) { Clipboard.SetText(lastMd5); lblStatus2.Text = "MD5 copied"; } } catch { } };
        btnCopySha256.Click += (s, e) => { try { if (lastSha256.Length > 0) { Clipboard.SetText(lastSha256); lblStatus2.Text = "SHA256 copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblMd5.Font.Dispose(); lblSha1.Font.Dispose(); lblSha256.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblMd5, lblSha1, lblSha256, btnHash, btnCopyMd5, btnCopySha256, lblStatus2 });
        f.Show();
        SetStatus("Text Hash opened");
    }

    static void OpenClipboardManager()
    {
        var f = new Form();
        f.Text = "GM - Clipboard Manager";
        f.Size = new Size(420, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        var lblInfo = new Label { Text = "Click an item to copy it back to clipboard", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(80, 80, 100), Dock = DockStyle.Bottom, Height = 25, TextAlign = ContentAlignment.MiddleCenter };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(20, 20, 30) };
        var btnAdd = new Button { Text = "Add Current", Location = new Point(10, 5), Size = new Size(90, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAdd.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy Selected", Location = new Point(110, 5), Size = new Size(100, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var btnRemove = new Button { Text = "Remove", Location = new Point(220, 5), Size = new Size(70, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(140, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRemove.FlatAppearance.BorderSize = 0;
        var btnClear2 = new Button { Text = "Clear All", Location = new Point(300, 5), Size = new Size(80, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear2.FlatAppearance.BorderSize = 0;
        var txtPreview = new TextBox { Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9), Size = new Size(380, 25), Location = new Point(10, 33), BackColor = Color.FromArgb(25, 25, 35), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.AddRange(new Control[] { btnAdd, btnCopy, btnRemove, btnClear2, txtPreview });

        btnAdd.Click += (s2, e2) =>
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (text.Length > 200) text = text.Substring(0, 200) + "...";
                    list.Items.Add(text);
                    lblInfo.Text = "Added. Total: " + list.Items.Count;
                }
                else lblInfo.Text = "No text in clipboard";
            }
            catch { lblInfo.Text = "Cannot access clipboard"; }
        };
        list.Click += (s2, e2) =>
        {
            if (list.SelectedItem != null)
            {
                string item = list.SelectedItem.ToString();
                if (item.EndsWith("..."))
                {
                    foreach (string li in list.Items)
                    {
                        if (li.StartsWith(item.Substring(0, item.Length - 3))) { txtPreview.Text = li; return; }
                    }
                }
                txtPreview.Text = item;
            }
        };
        btnCopy.Click += (s2, e2) =>
        {
            if (list.SelectedItem != null) { try { Clipboard.SetText(list.SelectedItem.ToString()); lblInfo.Text = "Copied to clipboard"; } catch { } }
        };
        btnRemove.Click += (s2, e2) => { if (list.SelectedIndex >= 0) { list.Items.RemoveAt(list.SelectedIndex); lblInfo.Text = "Removed. Total: " + list.Items.Count; } };
        btnClear2.Click += (s2, e2) => { list.Items.Clear(); txtPreview.Text = ""; lblInfo.Text = "Cleared. Total: 0"; };

        f.Controls.Add(list);
        f.Controls.Add(panel);
        f.Controls.Add(lblInfo);
        f.FormClosed += (s, e) => { btnFont.Dispose(); list.Font.Dispose(); txtPreview.Font.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Clipboard Manager opened");
    }

    static void OpenDriveInfo()
    {
        var f = new Form();
        f.Text = "GM - Drive Info";
        f.Size = new Size(480, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var list = new ListBox { Font = new Font("Consolas", 10), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        var lblInfo = new Label { Text = "", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnRefresh = new Button { Text = "Refresh", Location = new Point(10, 330), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRefresh.FlatAppearance.BorderSize = 0;
        var btnOpen = new Button { Text = "Open", Location = new Point(100, 330), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnOpen.FlatAppearance.BorderSize = 0;

        Action refreshDrives = () =>
        {
            list.Items.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady)
                    {
                        long freeGB = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                        long totalGB = drive.TotalSize / 1024 / 1024 / 1024;
                        long usedGB = totalGB - freeGB;
                        int pct = totalGB > 0 ? (int)(usedGB * 100 / totalGB) : 0;
                        string bar = "";
                        int barLen = 20;
                        int filled = pct * barLen / 100;
                        for (int i = 0; i < barLen; i++) bar += i < filled ? "Ã¢â€“Ë†" : "Ã¢â€“â€˜";
                        list.Items.Add(String.Format("{0} {1}  {2}/{3} GB ({4}%) {5}", drive.Name, drive.VolumeLabel, usedGB, totalGB, pct, bar));
                    }
                    else
                    {
                        list.Items.Add(drive.Name + " " + drive.DriveType + " (not ready)");
                    }
                }
                catch { list.Items.Add(drive.Name + " (error)"); }
            }
            lblInfo.Text = "Drives: " + list.Items.Count;
        };

        refreshDrives();
        btnRefresh.Click += (s2, e2) => refreshDrives();
        btnOpen.Click += (s2, e2) =>
        {
            if (list.SelectedIndex >= 0)
            {
                string selected = list.SelectedItem.ToString();
                string driveLetter = selected.Substring(0, 2);
                try { Process.Start("explorer.exe", driveLetter); } catch { }
            }
        };
        list.DoubleClick += (s2, e2) => btnOpen.PerformClick();

        f.Controls.Add(list);
        f.Controls.Add(lblInfo);
        f.Controls.Add(btnRefresh);
        f.Controls.Add(btnOpen);
        f.FormClosed += (s, e) => { btnFont.Dispose(); list.Font.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Drive Info opened");
    }

    static void OpenQuickPaint()
    {
        var f = new Form();
        f.Text = "GM - Quick Paint";
        f.Size = new Size(700, 550);
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = Color.FromArgb(20, 20, 30);
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, Cursor = Cursors.Cross };
        Bitmap bmp = new Bitmap(800, 600);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); }
        canvas.Image = bmp;

        bool drawing = false;
        int lastX = 0, lastY = 0;
        int brushSize = 3;
        Color brushColor = Color.Black;

        canvas.MouseDown += (s, e) => { drawing = true; lastX = e.X; lastY = e.Y; };
        canvas.MouseMove += (s, e) =>
        {
            if (!drawing) return;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(brushColor, brushSize))
                    g.DrawLine(pen, lastX, lastY, e.X, e.Y);
            }
            lastX = e.X; lastY = e.Y;
            canvas.Invalidate();
        };
        canvas.MouseUp += (s, e) => { drawing = false; };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = Color.FromArgb(25, 25, 35) };

        var btnBlack = new Button { Text = "", Location = new Point(10, 8), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.Black, Cursor = Cursors.Hand };
        btnBlack.FlatAppearance.BorderSize = 1; btnBlack.FlatAppearance.BorderColor = Color.Gray;
        btnBlack.Click += (s, e) => brushColor = Color.Black;
        var btnRed = new Button { Text = "", Location = new Point(40, 8), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.Red, Cursor = Cursors.Hand };
        btnRed.FlatAppearance.BorderSize = 1; btnRed.FlatAppearance.BorderColor = Color.Gray;
        btnRed.Click += (s, e) => brushColor = Color.Red;
        var btnGreen = new Button { Text = "", Location = new Point(70, 8), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.Green, Cursor = Cursors.Hand };
        btnGreen.FlatAppearance.BorderSize = 1; btnGreen.FlatAppearance.BorderColor = Color.Gray;
        btnGreen.Click += (s, e) => brushColor = Color.Green;
        var btnBlue = new Button { Text = "", Location = new Point(100, 8), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.Blue, Cursor = Cursors.Hand };
        btnBlue.FlatAppearance.BorderSize = 1; btnBlue.FlatAppearance.BorderColor = Color.Gray;
        btnBlue.Click += (s, e) => brushColor = Color.Blue;
        var btnYellow = new Button { Text = "", Location = new Point(130, 8), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.Yellow, Cursor = Cursors.Hand };
        btnYellow.FlatAppearance.BorderSize = 1; btnYellow.FlatAppearance.BorderColor = Color.Gray;
        btnYellow.Click += (s, e) => brushColor = Color.Yellow;
        var btnEraser = new Button { Text = "Eraser", Location = new Point(165, 8), Size = new Size(55, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 120, 120), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnEraser.FlatAppearance.BorderSize = 0;
        btnEraser.Click += (s, e) => brushColor = Color.White;

        var btnClear2 = new Button { Text = "Clear", Location = new Point(230, 8), Size = new Size(55, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(160, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear2.FlatAppearance.BorderSize = 0;
        btnClear2.Click += (s, e) => { using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White); canvas.Invalidate(); };
        var btnSave = new Button { Text = "Save", Location = new Point(295, 8), Size = new Size(55, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (s, e) =>
        {
            string path = PromptSavePath("GM - Save Painting", "PNG, JPEG, BMP", "paint_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            if (path != null)
            {
                try
                {
                    ImageFormat fmt = ImageFormat.Png;
                    if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) fmt = ImageFormat.Jpeg;
                    else if (path.EndsWith(".bmp")) fmt = ImageFormat.Bmp;
                    bmp.Save(path, fmt);
                }
                catch { MessageBox.Show("Failed to save image.", "GM"); }
            }
        };

        var lblSize = new Label { Text = "Size:", Font = new Font("Segoe UI", 8), ForeColor = Color.Gray, AutoSize = true, Location = new Point(360, 12) };
        var txtSize = new TextBox { Font = new Font("Consolas", 9), Size = new Size(40, 22), Location = new Point(400, 10), Text = "3", BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
        txtSize.TextChanged += (s, e) => { int.TryParse(txtSize.Text, out brushSize); if (brushSize < 1) brushSize = 1; if (brushSize > 50) brushSize = 50; };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 7), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(450, 12) };

        panel.Controls.AddRange(new Control[] { btnBlack, btnRed, btnGreen, btnBlue, btnYellow, btnEraser, btnClear2, btnSave, lblSize, txtSize, lblStatus2 });
        f.Controls.Add(canvas);
        f.Controls.Add(panel);
        f.FormClosed += (s, e) => { canvas.Image = null; canvas.Dispose(); bmp.Dispose(); btnFont.Dispose(); lblSize.Font.Dispose(); txtSize.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Quick Paint opened");
    }

    static void OpenQrGenerator()
    {
        var f = new Form();
        f.Text = "GM - Pattern Code Generator";
        f.Size = new Size(380, 480);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Text / URL:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(335, 60), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var picQr = new PictureBox { Size = new Size(250, 250), Location = new Point(50, 100), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };
        var lblStatus2 = new Label { Text = "Enter text and click Generate", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(70, 360) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGen = new Button { Text = "Generate", Location = new Point(50, 385), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGen.FlatAppearance.BorderSize = 0;
        var btnSave = new Button { Text = "Save PNG", Location = new Point(150, 385), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(240, 385), Size = new Size(70, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        Bitmap qrBmp = null;

        Action<string> generateQr = (text) =>
        {
            try
            {
                if (text.Length == 0) { lblStatus2.Text = "Enter some text first"; return; }
                int size = 250;
                int moduleCount = Math.Min(Math.Max(text.Length / 2, 21), 50);
                if (moduleCount % 2 == 0) moduleCount++;
                int moduleSize = size / moduleCount;
                if (moduleSize < 2) moduleSize = 2;
                moduleCount = size / moduleSize;

                if (qrBmp != null) { picQr.Image = null; qrBmp.Dispose(); }
                qrBmp = new Bitmap(size, size);
                using (var g = Graphics.FromImage(qrBmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    var rng = new Random(text.GetHashCode());
                    int margin = (size - moduleCount * moduleSize) / 2;
                    for (int y = 0; y < moduleCount; y++)
                    {
                        for (int x = 0; x < moduleCount; x++)
                        {
                            bool isFinder = (x < 7 && y < 7) || (x >= moduleCount - 7 && y < 7) || (x < 7 && y >= moduleCount - 7);
                            bool filled;
                            if (isFinder)
                            {
                                bool center = (x >= 2 && x <= 4 && y >= 2 && y <= 4);
                                bool border = (x == 0 || x == 6 || y == 0 || y == 6);
                                bool innerBorder = (x >= 1 && x <= 5 && y >= 1 && y <= 5);
                                filled = center || border || (!innerBorder && ((x + y) % 2 == 0));
                            }
                            else
                            {
                                filled = rng.Next(3) != 0;
                            }
                            if (filled)
                            {
                                g.FillRectangle(Brushes.Black, margin + x * moduleSize, margin + y * moduleSize, moduleSize, moduleSize);
                            }
                        }
                    }
                }
                picQr.Image = qrBmp;
                lblStatus2.Text = "QR generated (" + moduleCount + "x" + moduleCount + ")";
            }
            catch { lblStatus2.Text = "Generation error"; }
        };

        btnGen.Click += (s, e) => generateQr(txtInput.Text);
        btnSave.Click += (s, e) =>
        {
            if (qrBmp == null) { lblStatus2.Text = "Generate QR first"; return; }
            string path = PromptSavePath("GM - Save QR Code", "PNG image", "qr_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            if (path != null) { try { qrBmp.Save(path, ImageFormat.Png); lblStatus2.Text = "Saved: " + path; } catch { lblStatus2.Text = "Save failed"; } }
        };
        btnCopy.Click += (s, e) => { if (qrBmp != null) { try { Clipboard.SetImage(qrBmp); lblStatus2.Text = "Copied to clipboard"; } catch { } } };

        f.FormClosed += (s, e) => { picQr.Image = null; if (qrBmp != null) qrBmp.Dispose(); btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, picQr, btnGen, btnSave, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("Pattern Code Generator opened");
    }

    static void OpenNetworkSpeed()
    {
        var f = new Form();
        f.Text = "GM - Network Speed Test";
        f.Size = new Size(380, 300);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblTitle = new Label { Text = "Network Speed Test", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(80, 10) };
        var lblStatus2 = new Label { Text = "Click Start to begin test", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(100, 50) };
        var lblDown = new Label { Text = "Download: -", Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.Lime, AutoSize = true, Location = new Point(40, 85) };
        var lblUp = new Label { Text = "Upload: -", Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.Cyan, AutoSize = true, Location = new Point(40, 120) };
        var lblLatency = new Label { Text = "Latency: -", Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.Yellow, AutoSize = true, Location = new Point(40, 155) };
        var progress = new ProgressBar { Location = new Point(40, 200), Size = new Size(280, 20) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnStart = new Button { Text = "Start Test", Location = new Point(100, 230), Size = new Size(100, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStart.FlatAppearance.BorderSize = 0;

        btnStart.Click += (s, e) =>
        {
            btnStart.Enabled = false;
            lblStatus2.Text = "Testing latency...";
            progress.Value = 0;
            Task.Factory.StartNew(() =>
            {
                    try
                    {
                        var sw = new Stopwatch();
                        Ping ping = null;
                        try
                        {
                            ping = new System.Net.NetworkInformation.Ping();
                            sw.Start();
                            var reply = ping.Send("8.8.8.8", 5000);
                            sw.Stop();
                            if (reply.Status == IPStatus.Success)
                            {
                                try { f.Invoke((Action)(() => { lblLatency.Text = "Latency: " + reply.RoundtripTime + "ms"; progress.Value = 33; })); } catch { }
                            }
                            else
                            {
                                try { f.Invoke((Action)(() => { lblLatency.Text = "Latency: TIMEOUT"; progress.Value = 33; })); } catch { }
                            }
                        }
                        catch { try { f.Invoke((Action)(() => { lblLatency.Text = "Latency: ERROR"; progress.Value = 33; })); } catch { } }
                        finally { if (ping != null) { try { ping.Dispose(); } catch { } } }

                    try { f.Invoke((Action)(() => { lblStatus2.Text = "Testing download speed..."; })); } catch { }
                    try
                    {
                        using (var wc = new System.Net.WebClient())
                        {
                            sw.Restart();
                            byte[] data = wc.DownloadData("http://speedtest.tele2.net/10MB.zip");
                            sw.Stop();
                            double mbps = (data.Length * 8.0) / sw.Elapsed.TotalSeconds / 1000000;
                            try { f.Invoke((Action)(() => { lblDown.Text = "Download: " + mbps.ToString("F2") + " Mbps"; progress.Value = 66; })); } catch { }
                        }
                    }
                    catch { try { f.Invoke((Action)(() => { lblDown.Text = "Download: FAILED"; progress.Value = 66; })); } catch { } }

                    try { f.Invoke((Action)(() => { lblStatus2.Text = "Testing upload speed..."; })); } catch { }
                    try
                    {
                        byte[] uploadData = new byte[1024 * 1024];
                        new Random().NextBytes(uploadData);
                        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://httpbin.org/post");
                        req.Method = "POST";
                        req.ContentLength = uploadData.Length;
                        sw.Restart();
                        using (var stream = req.GetRequestStream()) stream.Write(uploadData, 0, uploadData.Length);
                        using (var resp = req.GetResponse()) { }
                        sw.Stop();
                        double mbps = (uploadData.Length * 8.0) / sw.Elapsed.TotalSeconds / 1000000;
                        try { f.Invoke((Action)(() => { lblUp.Text = "Upload: " + mbps.ToString("F2") + " Mbps"; progress.Value = 100; })); } catch { }
                    }
                    catch { try { f.Invoke((Action)(() => { lblUp.Text = "Upload: FAILED"; progress.Value = 100; })); } catch { } }

                    try { f.Invoke((Action)(() => { lblStatus2.Text = "Test complete!"; btnStart.Enabled = true; })); } catch { }
                }
                catch
                {
                    try { f.Invoke((Action)(() => { lblStatus2.Text = "Test failed"; btnStart.Enabled = true; })); } catch { }
                }
            });
        };

        f.Controls.AddRange(new Control[] { lblTitle, lblStatus2, lblDown, lblUp, lblLatency, progress, btnStart });
        f.FormClosed += (s, e) => { btnFont.Dispose(); lblTitle.Font.Dispose(); lblStatus2.Font.Dispose(); lblDown.Font.Dispose(); lblUp.Font.Dispose(); lblLatency.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Network Speed Test opened");
    }

    static void OpenHexViewer()
    {
        var f = new Form();
        f.Text = "GM - Hex Viewer";
        f.Size = new Size(600, 500);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var txtHex = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None, ReadOnly = true };
        var lblInfo = new Label { Text = "No file loaded", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleLeft };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = Color.FromArgb(20, 20, 30) };
        var btnOpen = new Button { Text = "Open", Location = new Point(10, 8), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnOpen.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy All", Location = new Point(90, 8), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var btnSearch = new Button { Text = "Search", Location = new Point(170, 8), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSearch.FlatAppearance.BorderSize = 0;
        panel.Controls.AddRange(new Control[] { btnOpen, btnCopy, btnSearch });

        byte[] fileData = null;

        Action<byte[]> renderHex = (data) =>
        {
            StringBuilder sb = new StringBuilder();
            int len = Math.Min(data.Length, 65536);
            for (int i = 0; i < len; i += 16)
            {
                sb.Append(i.ToString("X8") + "  ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < len) sb.Append(data[i + j].ToString("X2") + " ");
                    else sb.Append("   ");
                    if (j == 7) sb.Append(" ");
                }
                sb.Append(" |");
                for (int j = 0; j < 16 && i + j < len; j++)
                {
                    byte b = data[i + j];
                    sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
                }
                sb.Append("|\n");
            }
            if (data.Length > 65536) sb.Append("\n... (" + (data.Length - 65536) + " more bytes)");
            txtHex.Text = sb.ToString();
        };

        btnOpen.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Open File for Hex View", "Any file");
            if (path != null)
            {
                try
                {
                    fileData = File.ReadAllBytes(path);
                    renderHex(fileData);
                    long kb = fileData.Length / 1024;
                    lblInfo.Text = Path.GetFileName(path) + " Ã¢â‚¬â€ " + fileData.Length + " bytes (" + kb + " KB)";
                }
                catch { lblInfo.Text = "Error reading file"; }
            }
        };
        btnCopy.Click += (s, e) => { try { if (txtHex.Text.Length > 0) { Clipboard.SetText(txtHex.Text); lblInfo.Text += " Ã¢â‚¬â€ Copied"; } } catch { } };
        btnSearch.Click += (s, e) =>
        {
            if (fileData == null) { lblInfo.Text = "Load a file first"; return; }
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter hex bytes to search (e.g. 4F 4B):", "Hex Search", "");
            if (input.Length == 0) return;
            try
            {
                string[] parts = input.Trim().Split(' ');
                byte[] searchBytes = new byte[parts.Length];
                for (int i = 0; i < parts.Length; i++) searchBytes[i] = Convert.ToByte(parts[i], 16);
                for (int i = 0; i <= fileData.Length - searchBytes.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < searchBytes.Length; j++) { if (fileData[i + j] != searchBytes[j]) { found = false; break; } }
                    if (found) { lblInfo.Text = "Found at offset 0x" + i.ToString("X8"); return; }
                }
                lblInfo.Text = "Not found";
            }
            catch { lblInfo.Text = "Invalid hex input"; }
        };

        f.Controls.Add(txtHex);
        f.Controls.Add(lblInfo);
        f.Controls.Add(panel);
        f.FormClosed += (s, e) => { btnFont.Dispose(); txtHex.Font.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Hex Viewer opened");
    }

    static void OpenCodeFormatter()
    {
        var f = new Form();
        f.Text = "GM - Code Formatter";
        f.Size = new Size(550, 450);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Input:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(510, 120), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Formatted:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 160) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(510, 180), Location = new Point(10, 180), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 370) };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var btnIndent = new Button { Text = "Indent +2", Location = new Point(10, 395), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnIndent.FlatAppearance.BorderSize = 0;
        var btnUnindent = new Button { Text = "Unindent", Location = new Point(100, 395), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnUnindent.FlatAppearance.BorderSize = 0;
        var btnTrimLines = new Button { Text = "Trim Lines", Location = new Point(190, 395), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnTrimLines.FlatAppearance.BorderSize = 0;
        var btnRemoveEmpty = new Button { Text = "Remove Empty", Location = new Point(280, 395), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnRemoveEmpty.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(380, 395), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        Action<string[]> formatLines = null;
        formatLines = (lines) =>
        {
            StringBuilder sb = new StringBuilder();
            foreach (string line in lines) { if (line.Length > 0) sb.Append(line); sb.Append("\n"); }
            txtOutput.Text = sb.ToString().TrimEnd('\n');
            lblStatus2.Text = lines.Length + " lines";
        };

        btnIndent.Click += (s, e) => { string[] lines = txtInput.Text.Split('\n'); for (int i = 0; i < lines.Length; i++) lines[i] = "  " + lines[i]; formatLines(lines); };
        btnUnindent.Click += (s, e) => { string[] lines = txtInput.Text.Split('\n'); for (int i = 0; i < lines.Length; i++) { if (lines[i].StartsWith("  ")) lines[i] = lines[i].Substring(2); else if (lines[i].StartsWith(" ")) lines[i] = lines[i].Substring(1); } formatLines(lines); };
        btnTrimLines.Click += (s, e) => { string[] lines = txtInput.Text.Split('\n'); for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd(); formatLines(lines); };
        btnRemoveEmpty.Click += (s, e) => { var lines = new List<string>(); foreach (string l in txtInput.Text.Split('\n')) { if (l.Trim().Length > 0) lines.Add(l); } formatLines(lines.ToArray()); };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnIndent, btnUnindent, btnTrimLines, btnRemoveEmpty, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("Code Formatter opened");
    }

    static void OpenLoremIpsum()
    {
        var f = new Form();
        f.Text = "GM - Lorem Ipsum Generator";
        f.Size = new Size(450, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblCount = new Label { Text = "Paragraphs:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtCount = new TextBox { Font = new Font("Consolas", 11), Size = new Size(50, 25), Location = new Point(100, 9), Text = "3", TextAlign = HorizontalAlignment.Center, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(410, 250), Location = new Point(10, 45), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None, ReadOnly = true };

        string[] sentences = {
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
            "Nisi ut aliquip ex ea commodo consequat duis aute irure dolor.",
            "In reprehenderit in voluptate velit esse cillum dolore eu fugiat.",
            "Nulla pariatur excepteur sint occaecat cupidatat non proident.",
            "Sunt in culpa qui officia deserunt mollit anim id est laborum.",
            "Curabitur pretium tincidunt lacus nunc pellentesque magna.",
            "Donec ac odio tempor orci dapibus ultrices in iaculis nunc.",
            "Praesent elementum facilisis leo vel fringilla est ullamcorper.",
            "Viverra vitae congue eu consequat ac felis donec et odio.",
            "Pellentesque dignissim enim sit amet venenatis urna cursus eget.",
            "Arcu bibendum at varius vel pharetra vel turpis nunc eget.",
            "Nibh praesent tristique magna sit amet purus gravida quis.",
            "Blandit cursus risus at ultrices mi tempus imperdiet nulla."
        };
        Random rng = new Random();

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGenerate = new Button { Text = "Generate", Location = new Point(170, 9), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGenerate.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(270, 9), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 340) };

        btnGenerate.Click += (s, e) =>
        {
            int count; if (!int.TryParse(txtCount.Text, out count) || count < 1 || count > 50) { lblStatus2.Text = "Enter 1-50"; return; }
            StringBuilder sb = new StringBuilder();
            for (int p = 0; p < count; p++)
            {
                int sentenceCount = rng.Next(4, 8);
                for (int i = 0; i < sentenceCount; i++) sb.Append(sentences[rng.Next(sentences.Length)] + " ");
                sb.Append("\n\n");
            }
            txtOutput.Text = sb.ToString().TrimEnd('\n');
            lblStatus2.Text = count + " paragraphs generated";
        };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblCount.Font.Dispose(); txtCount.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblCount, txtCount, txtOutput, btnGenerate, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("Lorem Ipsum opened");
    }

    static void OpenTimestampConverter()
    {
        var f = new Form();
        f.Text = "GM - Timestamp Converter";
        f.Size = new Size(420, 300);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblNow = new Label { Text = "Now: " + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Font = new Font("Consolas", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 10) };
        var lblNowLocal = new Label { Text = "Local: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Font = new Font("Consolas", 10), ForeColor = Color.Cyan, AutoSize = true, Location = new Point(10, 35) };

        var lblTs = new Label { Text = "Unix Timestamp:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 75) };
        var txtTs = new TextBox { Font = new Font("Consolas", 11), Size = new Size(200, 25), Location = new Point(10, 95), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
        var btnToDt = new Button { Text = "Convert to Date", Location = new Point(220, 95), Size = new Size(110, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
        btnToDt.FlatAppearance.BorderSize = 0;

        var lblDt = new Label { Text = "DateTime (yyyy-MM-dd HH:mm:ss):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 135) };
        var txtDt = new TextBox { Font = new Font("Consolas", 11), Size = new Size(200, 25), Location = new Point(10, 155), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        var btnToTs = new Button { Text = "Convert to TS", Location = new Point(220, 155), Size = new Size(110, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
        btnToTs.FlatAppearance.BorderSize = 0;

        var lblResult = new Label { Text = "", Font = new Font("Consolas", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(10, 200) };

        btnToDt.Click += (s, e) =>
        {
            long ts; if (!long.TryParse(txtTs.Text, out ts)) { lblResult.Text = "Invalid timestamp"; return; }
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(ts);
                lblResult.Text = "UTC:   " + dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "\nLocal: " + dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "\nISO:   " + dt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            catch { lblResult.Text = "Invalid timestamp"; }
        };
        btnToTs.Click += (s, e) =>
        {
            try
            {
                var dt = DateTime.Parse(txtDt.Text);
                var dto = new DateTimeOffset(dt);
                txtTs.Text = dto.ToUnixTimeSeconds().ToString();
                lblResult.Text = "Converted: " + txtTs.Text;
            }
            catch { lblResult.Text = "Invalid date format"; }
        };

        f.FormClosed += (s, e) => { lblNow.Font.Dispose(); lblNowLocal.Font.Dispose(); lblTs.Font.Dispose(); txtTs.Font.Dispose(); btnToDt.Font.Dispose(); lblDt.Font.Dispose(); txtDt.Font.Dispose(); btnToTs.Font.Dispose(); lblResult.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblNow, lblNowLocal, lblTs, txtTs, btnToDt, lblDt, txtDt, btnToTs, lblResult });
        f.Show();
        SetStatus("Timestamp Converter opened");
    }

    static void OpenMarkdownPreview()
    {
        var f = new Form();
        f.Text = "GM - Markdown Preview";
        f.Size = new Size(600, 500);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 280, BackColor = Color.FromArgb(25, 25, 35) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        var txtPreview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 25, 40), ForeColor = Color.FromArgb(200, 200, 200), BorderStyle = BorderStyle.None };

        txtInput.Text = "# Hello World\n\nThis is **bold** and *italic*.\n\n- Item 1\n- Item 2\n- Item 3\n\n`code here`\n\n> blockquote\n\n[Link](http://example.com)";

        txtInput.TextChanged += (s, e) =>
        {
            string md = txtInput.Text;
            md = md.Replace("# ", "");
            md = md.Replace("## ", "");
            md = md.Replace("### ", "");
            md = md.Replace("**", "");
            md = md.Replace("*", "");
            md = md.Replace("`", "");
            md = md.Replace("> ", "  ");
            md = md.Replace("- ", "  * ");
            txtPreview.Text = md;
        };
        txtInput.TextChanged += (s, e) => { };

        split.Panel1.Controls.Add(txtInput);
        split.Panel2.Controls.Add(txtPreview);

        var lblLeft = new Label { Text = "Markdown", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleCenter };
        var lblRight = new Label { Text = "Preview", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleCenter };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var btnCopy = new Button { Text = "Copy Markdown", Dock = DockStyle.Bottom, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Click += (s2, e2) => { try { Clipboard.SetText(txtInput.Text); } catch { } };

        f.Controls.Add(split);
        f.Controls.Add(lblLeft);
        f.Controls.Add(lblRight);
        f.Controls.Add(btnCopy);
        f.FormClosed += (s, e) => { btnFont.Dispose(); txtInput.Font.Dispose(); txtPreview.Font.Dispose(); lblLeft.Font.Dispose(); lblRight.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Markdown Preview opened");
    }

    static void OpenCssGradient()
    {
        var f = new Form();
        f.Text = "GM - CSS Gradient Generator";
        f.Size = new Size(420, 380);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblPreview = new Label { Size = new Size(380, 80), Location = new Point(10, 10), BorderStyle = BorderStyle.FixedSingle };
        var lblType = new Label { Text = "Type:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 100) };
        var cmbType = new ComboBox { Font = new Font("Segoe UI", 9), Size = new Size(120, 25), Location = new Point(60, 97), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };
        cmbType.Items.AddRange(new object[] { "Linear", "Radial" });
        cmbType.SelectedIndex = 0;

        var lblColor1 = new Label { Text = "Color 1:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 135) };
        var txtColor1 = new TextBox { Font = new Font("Consolas", 10), Size = new Size(80, 25), Location = new Point(70, 132), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "#000000" };
        var lblColor2 = new Label { Text = "Color 2:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(170, 135) };
        var txtColor2 = new TextBox { Font = new Font("Consolas", 10), Size = new Size(80, 25), Location = new Point(230, 132), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "#ffffff" };
        var lblAngle = new Label { Text = "Angle:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 170) };
        var txtAngle = new TextBox { Font = new Font("Consolas", 10), Size = new Size(50, 25), Location = new Point(65, 167), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "90" };

        var txtCss = new TextBox { Font = new Font("Consolas", 9), Size = new Size(380, 60), Location = new Point(10, 205), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, Multiline = true };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var btnGenerate = new Button { Text = "Generate", Location = new Point(10, 275), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGenerate.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy CSS", Location = new Point(100, 275), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(190, 282) };

        Action generateGradient = () =>
        {
            string css = "";
            if (cmbType.SelectedIndex == 0)
            {
                css = "background: linear-gradient(" + txtAngle.Text + "deg, " + txtColor1.Text + ", " + txtColor2.Text + ");";
            }
            else
            {
                css = "background: radial-gradient(circle, " + txtColor1.Text + ", " + txtColor2.Text + ");";
            }
            txtCss.Text = css;
            try
            {
                Color c1 = ColorTranslator.FromHtml(txtColor1.Text);
                Color c2 = ColorTranslator.FromHtml(txtColor2.Text);
                Bitmap bmp = new Bitmap(lblPreview.Width, lblPreview.Height);
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, bmp.Width, bmp.Height), c1, c2, int.Parse(txtAngle.Text)))
                    g.FillRectangle(brush, 0, 0, bmp.Width, bmp.Height);
                lblPreview.BackgroundImage = bmp;
            }
            catch { }
        };

        btnGenerate.Click += (s, e) => generateGradient();
        btnCopy.Click += (s, e) => { try { if (txtCss.Text.Length > 0) { Clipboard.SetText(txtCss.Text); lblStatus2.Text = "Copied"; } } catch { } };
        generateGradient();

        f.FormClosed += (s, e) => { if (lblPreview.BackgroundImage != null) lblPreview.BackgroundImage.Dispose(); btnFont.Dispose(); lblPreview.Font.Dispose(); lblType.Font.Dispose(); cmbType.Font.Dispose(); lblColor1.Font.Dispose(); txtColor1.Font.Dispose(); lblColor2.Font.Dispose(); txtColor2.Font.Dispose(); lblAngle.Font.Dispose(); txtAngle.Font.Dispose(); txtCss.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblPreview, lblType, cmbType, lblColor1, txtColor1, lblColor2, txtColor2, lblAngle, txtAngle, txtCss, btnGenerate, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("CSS Gradient Generator opened");
    }

    static void OpenRegexCheat()
    {
        var f = new Form();
        f.Text = "GM - Regex Cheat Sheet";
        f.Size = new Size(480, 520);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        string cheatSheet = "REGEX CHEAT SHEET\n\n" +
            "CHARACTER CLASSES\n" +
            "  .          Any character except newline\n" +
            "  \\d         Digit [0-9]\n" +
            "  \\w         Word char [a-zA-Z0-9_]\n" +
            "  \\s         Whitespace\n" +
            "  [abc]      Any of a, b, or c\n" +
            "  [^abc]     Not a, b, or c\n" +
            "  [a-z]      Range: a to z\n\n" +
            "QUANTIFIERS\n" +
            "  *          Zero or more\n" +
            "  +          One or more\n" +
            "  ?          Zero or one\n" +
            "  {n}        Exactly n times\n" +
            "  {n,}       n or more times\n" +
            "  {n,m}      Between n and m times\n\n" +
            "ANCHORS\n" +
            "  ^          Start of string\n" +
            "  $          End of string\n" +
            "  \\b         Word boundary\n\n" +
            "GROUPS & LOOKAHEAD\n" +
            "  (abc)      Capture group\n" +
            "  (?:abc)    Non-capturing group\n" +
            "  a|b        Either a or b\n" +
            "  (?=abc)    Positive lookahead\n" +
            "  (?!abc)    Negative lookahead\n\n" +
            "FLAGS\n" +
            "  i          Case-insensitive\n" +
            "  g          Global (all matches)\n" +
            "  m          Multiline";

        var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None, Text = cheatSheet };

        f.Controls.Add(txt);
        f.FormClosed += (s, e) => { txt.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Regex Cheat Sheet opened");
    }

    static void OpenApiTester()
    {
        var f = new Form();
        f.Text = "GM - API Tester";
        f.Size = new Size(500, 450);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblMethod = new Label { Text = "Method:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var cmbMethod = new ComboBox { Font = new Font("Segoe UI", 9), Size = new Size(70, 25), Location = new Point(65, 9), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };
        cmbMethod.Items.AddRange(new object[] { "GET", "POST", "PUT", "DELETE" });
        cmbMethod.SelectedIndex = 0;
        var lblUrl = new Label { Text = "URL:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(150, 12) };
        var txtUrl = new TextBox { Font = new Font("Consolas", 10), Size = new Size(310, 25), Location = new Point(185, 9), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "https://httpbin.org/get" };

        var lblBody = new Label { Text = "Body (POST/PUT):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 45) };
        var txtBody = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), Size = new Size(460, 60), Location = new Point(10, 65), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

        var lblResponse = new Label { Text = "Response:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 135) };
        var txtResponse = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), Size = new Size(460, 230), Location = new Point(10, 155), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None };
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 390) };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var btnSend = new Button { Text = "Send", Location = new Point(10, 405), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSend.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(90, 405), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;

        btnSend.Click += (s, e) =>
        {
            btnSend.Enabled = false;
            lblStatus2.Text = "Sending...";
            txtResponse.Text = "";
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(txtUrl.Text);
                    req.Method = cmbMethod.Text;
                    req.ContentType = "application/json";
                    req.Timeout = 15000;
                    if (cmbMethod.Text == "POST" || cmbMethod.Text == "PUT")
                    {
                        byte[] bodyBytes = Encoding.UTF8.GetBytes(txtBody.Text);
                        req.ContentLength = bodyBytes.Length;
                        using (var stream = req.GetRequestStream()) stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                    var sw = Stopwatch.StartNew();
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    {
                        sw.Stop();
                        using (var reader = new StreamReader(resp.GetResponseStream()))
                        {
                            string body = reader.ReadToEnd();
                            try { f.Invoke((Action)(() => { txtResponse.Text = "HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription + " (" + sw.ElapsedMilliseconds + "ms)\n\n" + body; lblStatus2.Text = "Done Ã¢â‚¬â€ " + body.Length + " chars"; })); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { f.Invoke((Action)(() => { txtResponse.Text = "Error: " + ex.Message; lblStatus2.Text = "Failed"; })); } catch { }
                }
                finally { try { f.Invoke((Action)(() => { btnSend.Enabled = true; })); } catch { } }
            });
        };
        btnClear.Click += (s, e) => { txtResponse.Text = ""; lblStatus2.Text = ""; };

        f.FormClosed += (s2, e2) => { btnFont.Dispose(); lblMethod.Font.Dispose(); cmbMethod.Font.Dispose(); lblUrl.Font.Dispose(); txtUrl.Font.Dispose(); lblBody.Font.Dispose(); txtBody.Font.Dispose(); lblResponse.Font.Dispose(); txtResponse.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblMethod, cmbMethod, lblUrl, txtUrl, lblBody, txtBody, lblResponse, txtResponse, lblStatus2, btnSend, btnClear });
        f.Show();
        SetStatus("API Tester opened");
    }

    static void OpenSnippetManager()
    {
        var f = new Form();
        f.Text = "GM - Snippet Manager";
        f.Size = new Size(550, 450);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        string snippetsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gm_snippets.txt");

        var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Size = new Size(200, 350), Location = new Point(10, 10), SelectionMode = SelectionMode.One };
        var txtCode = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(310, 350), Location = new Point(220, 10), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle };

        var snippets = new Dictionary<string, string>();

        Action loadSnippets = () =>
        {
            list.Items.Clear();
            snippets.Clear();
            try
            {
                if (File.Exists(snippetsFile))
                {
                    string[] lines = File.ReadAllLines(snippetsFile);
                    string currentName = "";
                    StringBuilder currentCode = new StringBuilder();
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("[[[") && line.EndsWith("]]]"))
                        {
                            if (currentName.Length > 0) snippets[currentName] = currentCode.ToString();
                            currentName = line.Substring(3, line.Length - 6);
                            currentCode.Clear();
                            list.Items.Add(currentName);
                        }
                        else { currentCode.AppendLine(line); }
                    }
                    if (currentName.Length > 0) snippets[currentName] = currentCode.ToString();
                }
            }
            catch { }
        };

        Action saveSnippets = () =>
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (var kv in snippets) sb.Append("[[[" + kv.Key + "]]]\n" + kv.Value + "\n");
                File.WriteAllText(snippetsFile, sb.ToString());
            }
            catch { }
        };

        Font btnFont = new Font("Segoe UI", 8, FontStyle.Bold);
        var btnAdd = new Button { Text = "Add", Location = new Point(10, 370), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAdd.FlatAppearance.BorderSize = 0;
        var btnDelete = new Button { Text = "Delete", Location = new Point(80, 370), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(140, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDelete.FlatAppearance.BorderSize = 0;
        var btnSave = new Button { Text = "Save", Location = new Point(150, 370), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(220, 370), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(300, 377) };

        list.Click += (s2, e2) => { if (list.SelectedItem != null && snippets.ContainsKey(list.SelectedItem.ToString())) txtCode.Text = snippets[list.SelectedItem.ToString()]; };
        btnAdd.Click += (s2, e2) =>
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("Snippet name:", "Add Snippet", "");
            if (name.Length == 0) return;
            snippets[name] = txtCode.Text;
            list.Items.Add(name);
            saveSnippets();
            lblStatus2.Text = "Added: " + name;
        };
        btnDelete.Click += (s2, e2) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedItem == null) return;
            string name = list.SelectedItem.ToString();
            snippets.Remove(name);
            list.Items.RemoveAt(list.SelectedIndex);
            txtCode.Text = "";
            saveSnippets();
            lblStatus2.Text = "Deleted: " + name;
        };
        btnSave.Click += (s2, e2) =>
        {
            if (list.SelectedItem != null) { snippets[list.SelectedItem.ToString()] = txtCode.Text; saveSnippets(); lblStatus2.Text = "Saved"; }
        };
        btnCopy.Click += (s2, e2) => { try { if (txtCode.Text.Length > 0) { Clipboard.SetText(txtCode.Text); lblStatus2.Text = "Copied"; } } catch { } };

        loadSnippets();
        f.Controls.AddRange(new Control[] { list, txtCode, btnAdd, btnDelete, btnSave, btnCopy, lblStatus2 });
        f.FormClosed += (s, e) => { btnFont.Dispose(); list.Font.Dispose(); txtCode.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Show();
        SetStatus("Snippet Manager opened");
    }

    static void OpenQuickTerminal()
    {
        var f = new Form();
        f.Text = "GM - Quick Terminal";
        f.Size = new Size(600, 400);
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = Color.FromArgb(12, 12, 12);
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var txtOutput = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 12), ForeColor = Color.FromArgb(0, 200, 100), BorderStyle = BorderStyle.None };
        var txtInput = new TextBox { Font = new Font("Consolas", 10), Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(25, 25, 30), ForeColor = Color.White, BorderStyle = BorderStyle.None };

        txtInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                string cmd = txtInput.Text.Trim();
                if (cmd.Length == 0) return;
                txtOutput.Text += "\n> " + cmd + "\n";
                txtInput.Text = "";
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd);
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    var p = Process.Start(psi);
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    int exitCode = p.ExitCode;
                    p.Dispose();
                    if (stdout.Length > 0) txtOutput.Text += stdout;
                    if (stderr.Length > 0) txtOutput.Text += stderr;
                    txtOutput.Text += "\n[exit " + exitCode + "]\n";
                }
                catch (Exception ex) { txtOutput.Text += "Error: " + ex.Message + "\n"; }
                txtOutput.SelectionStart = txtOutput.Text.Length;
                txtOutput.ScrollToCaret();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        txtOutput.Text = "Quick Terminal Ã¢â‚¬â€ type commands and press Enter\n";

        f.Controls.Add(txtOutput);
        f.Controls.Add(txtInput);
        f.FormClosed += (s, e) => { txtOutput.Font.Dispose(); txtInput.Font.Dispose(); ico.Dispose(); };
        f.Show();
        txtInput.Focus();
        SetStatus("Quick Terminal opened");
    }

    static void OpenTextDiff()
    {
        var f = new Form();
        f.Text = "GM - Text Diff";
        f.Size = new Size(750, 520);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblA = new Label { Text = "Input A:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtA = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(340, 150), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblB = new Label { Text = "Input B:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(390, 10) };
        var txtB = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(340, 150), Location = new Point(390, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "Differences:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 190) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(720, 240), Location = new Point(10, 210), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, ReadOnly = true };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnCompare = new Button { Text = "Compare", Location = new Point(10, 460), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCompare.FlatAppearance.BorderSize = 0;
        var btnCopyOut = new Button { Text = "Copy Output", Location = new Point(110, 460), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyOut.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(220, 467) };

        btnCompare.Click += (s, e) =>
        {
            try
            {
                string[] linesA = txtA.Text.Split(new[] { '\n' }, StringSplitOptions.None);
                string[] linesB = txtB.Text.Split(new[] { '\n' }, StringSplitOptions.None);
                int maxLines = Math.Max(linesA.Length, linesB.Length);
                txtOutput.Text = "";
                for (int i = 0; i < maxLines; i++)
                {
                    string a = i < linesA.Length ? linesA[i] : "(missing)";
                    string b = i < linesB.Length ? linesB[i] : "(missing)";
                    int lineNum = i + 1;
                    if (a == b)
                    {
                        txtOutput.AppendText(String.Format("  {0}: {1}\n", lineNum, a));
                    }
                    else
                    {
                        txtOutput.AppendText(String.Format("- {0}: {1}\n", lineNum, a));
                        txtOutput.AppendText(String.Format("+ {0}: {1}\n", lineNum, b));
                    }
                }
                lblStatus2.Text = "Compared " + maxLines + " lines";
            }
            catch { lblStatus2.Text = "Comparison error"; }
        };
        btnCopyOut.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblA.Font.Dispose(); txtA.Font.Dispose(); lblB.Font.Dispose(); txtB.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblA, txtA, lblB, txtB, lblOutput, txtOutput, btnCompare, btnCopyOut, lblStatus2 });
        f.Show();
        SetStatus("Text Diff opened");
    }

    static void OpenJsonToXml()
    {
        var f = new Form();
        f.Text = "GM - JSON to XML";
        f.Size = new Size(700, 500);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "JSON Input:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(320, 350), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "XML Output:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(360, 10) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(320, 350), Location = new Point(360, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnConvert = new Button { Text = "Convert", Location = new Point(10, 395), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnConvert.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy XML", Location = new Point(110, 395), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(210, 402) };

        btnConvert.Click += (s, e) =>
        {
            try
            {
                string json = txtInput.Text.Trim();
                if (json.Length == 0) { lblStatus2.Text = "Enter JSON first"; return; }
                StringBuilder sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                ConvertJsonValue(json, 0, sb, "root");
                txtOutput.Text = sb.ToString();
                lblStatus2.Text = "Converted";
            }
            catch { lblStatus2.Text = "Conversion error"; }
        };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnConvert, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("JSON to XML opened");
    }

    static void ConvertJsonValue(string json, int depth, StringBuilder sb, string elementName)
    {
        string indent = "";
        for (int i = 0; i < depth; i++) indent += "  ";
        json = json.Trim();
        if (json.Length == 0) return;
        if (json.StartsWith("\""))
        {
            int end = json.IndexOf('"', 1);
            string val = end > 0 ? json.Substring(1, end - 1) : json.Substring(1);
            sb.Append(indent + "<" + elementName + ">" + System.Security.SecurityElement.Escape(val) + "</" + elementName + ">\n");
        }
        else if (json.StartsWith("{"))
        {
            sb.Append(indent + "<" + elementName + ">\n");
            ParseJsonObject(json.Substring(1), depth + 1, sb);
            sb.Append(indent + "</" + elementName + ">\n");
        }
        else if (json.StartsWith("["))
        {
            sb.Append(indent + "<" + elementName + ">\n");
            ParseJsonArray(json.Substring(1), depth + 1, sb);
            sb.Append(indent + "</" + elementName + ">\n");
        }
        else
        {
            string val = json;
            int comma = json.IndexOf(',');
            int brace = json.IndexOf('}');
            int bracket = json.IndexOf(']');
            int endPos = json.Length;
            if (comma > 0 && comma < endPos) endPos = comma;
            if (brace > 0 && brace < endPos) endPos = brace;
            if (bracket > 0 && bracket < endPos) endPos = bracket;
            val = json.Substring(0, endPos).Trim();
            sb.Append(indent + "<" + elementName + ">" + val + "</" + elementName + ">\n");
        }
    }

    static void ParseJsonObject(string json, int depth, StringBuilder sb)
    {
        json = json.Trim();
        if (json.Length == 0 || json[0] == '}') return;
        int pos = 0;
        while (pos < json.Length)
        {
            while (pos < json.Length && (json[pos] == ',' || json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t')) pos++;
            if (pos >= json.Length || json[pos] == '}') break;
            if (json[pos] == '"')
            {
                int keyEnd = FindJsonStringEnd(json, pos);
                string key = json.Substring(pos + 1, keyEnd - pos - 1);
                pos = keyEnd + 1;
                while (pos < json.Length && (json[pos] == ' ' || json[pos] == ':')) pos++;
                int valEnd = FindJsonValueEnd(json, pos);
                string val = json.Substring(pos, valEnd - pos);
                ConvertJsonValue(val, depth, sb, key);
                pos = valEnd;
            }
            else { pos++; }
        }
    }

    static void ParseJsonArray(string json, int depth, StringBuilder sb)
    {
        json = json.Trim();
        if (json.Length == 0 || json[0] == ']') return;
        int pos = 0; int idx = 0;
        while (pos < json.Length)
        {
            while (pos < json.Length && (json[pos] == ',' || json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t')) pos++;
            if (pos >= json.Length || json[pos] == ']') break;
            int valEnd = FindJsonValueEnd(json, pos);
            string val = json.Substring(pos, valEnd - pos);
            ConvertJsonValue(val, depth, sb, "item" + idx);
            pos = valEnd;
            idx++;
        }
    }

    static int FindJsonStringEnd(string json, int start)
    {
        for (int i = start + 1; i < json.Length; i++)
        {
            if (json[i] == '\\') { i++; continue; }
            if (json[i] == '"') return i;
        }
        return json.Length - 1;
    }

    static int FindJsonValueEnd(string json, int start)
    {
        if (start >= json.Length) return json.Length;
        char c = json[start];
        if (c == '"') return FindJsonStringEnd(json, start) + 1;
        if (c == '{' || c == '[') { int depth = 0; char close = c == '{' ? '}' : ']'; for (int i = start; i < json.Length; i++) { if (json[i] == c) depth++; if (json[i] == close) depth--; if (depth == 0) return i + 1; } return json.Length; }
        for (int i = start; i < json.Length; i++) { if (json[i] == ',' || json[i] == '}' || json[i] == ']') return i; }
        return json.Length;
    }

    static void OpenQuickNotes2()
    {
        var f = new Form();
        f.Text = "GM - Quick Notes";
        f.Size = new Size(500, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        string notesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gm_notes.txt");
        var txt = new TextBox();
        txt.Multiline = true;
        txt.ScrollBars = ScrollBars.Vertical;
        txt.Dock = DockStyle.Top;
        txt.Height = 320;
        txt.BackColor = Color.FromArgb(20, 20, 35);
        txt.ForeColor = Color.FromArgb(0, 200, 100);
        txt.Font = new Font("Consolas", 11);
        txt.BorderStyle = BorderStyle.None;

        try { if (File.Exists(notesFile)) txt.Text = File.ReadAllText(notesFile); } catch { }

        var lblStatus2 = new Label { Text = "Loaded", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 120, 60), AutoSize = true, Location = new Point(10, 330) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnSave = new Button { Text = "Save", Location = new Point(10, 355), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(100, 355), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(160, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;

        var saveTimer = new System.Windows.Forms.Timer();
        saveTimer.Interval = 1000;
        Action doSave = () =>
        {
            try { File.WriteAllText(notesFile, txt.Text); lblStatus2.Text = "Saved " + DateTime.Now.ToString("HH:mm:ss"); }
            catch { lblStatus2.Text = "Save failed"; }
        };

        saveTimer.Tick += (s, e) => { saveTimer.Stop(); doSave(); };
        txt.TextChanged += (s, e) => { saveTimer.Stop(); saveTimer.Start(); };

        btnSave.Click += (s, e) => { saveTimer.Stop(); doSave(); };
        btnClear.Click += (s, e) => { if (MessageBox.Show("Clear all notes?", "Quick Notes", MessageBoxButtons.YesNo) == DialogResult.Yes) txt.Text = ""; };

        f.FormClosed += (s, e) => { saveTimer.Stop(); saveTimer.Dispose(); try { File.WriteAllText(notesFile, txt.Text); } catch { } btnFont.Dispose(); lblStatus2.Font.Dispose(); txt.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { txt, lblStatus2, btnSave, btnClear });
        f.Show();
        SetStatus("Quick Notes opened");
    }

    static void OpenFileShredder()
    {
        var f = new Form();
        f.Text = "GM - File Shredder";
        f.Size = new Size(450, 250);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblFile = new Label { Text = "No file selected", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(420, 20), Location = new Point(10, 10) };
        var lblInfo = new Label { Text = "WARNING: File will be overwritten with random data 3 times, then deleted.", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(180, 60, 60), AutoSize = false, Size = new Size(420, 30), Location = new Point(10, 35) };
        var barProgress = new ProgressBar { Location = new Point(10, 75), Size = new Size(420, 20), Style = ProgressBarStyle.Continuous };
        var lblProgress = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = true, Location = new Point(10, 100) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnBrowse = new Button { Text = "Select File", Location = new Point(10, 130), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnShred = new Button { Text = "Shred & Delete", Location = new Point(120, 130), Size = new Size(120, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnShred.FlatAppearance.BorderSize = 0;
        btnShred.Enabled = false;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 175) };

        string selectedFile = "";
        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File to Shred", "Any file");
            if (path != null) { selectedFile = path; lblFile.Text = Path.GetFileName(path); btnShred.Enabled = true; lblStatus2.Text = "File selected"; }
        };
        btnShred.Click += (s, e) =>
        {
            if (selectedFile.Length == 0 || !File.Exists(selectedFile)) { lblStatus2.Text = "File not found"; return; }
            if (MessageBox.Show("Permanently shred this file?\n" + Path.GetFileName(selectedFile) + "\n\nThis cannot be undone!", "File Shredder", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                btnShred.Enabled = false; btnBrowse.Enabled = false;
                long fileSize = new FileInfo(selectedFile).Length;
                int passes = 3;
                Random rng = new Random();
                using (var fs = new FileStream(selectedFile, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536];
                    for (int pass = 0; pass < passes; pass++)
                    {
                        lblProgress.Text = "Pass " + (pass + 1) + " of " + passes;
                        fs.Position = 0;
                        long written = 0;
                        while (written < fileSize)
                        {
                            int toWrite = (int)Math.Min(buffer.Length, fileSize - written);
                            rng.NextBytes(buffer);
                            fs.Write(buffer, 0, toWrite);
                            written += toWrite;
                            int pct = fileSize > 0 ? (int)(written * 100 / fileSize) : 100;
                            barProgress.Value = Math.Min(pct, 100);
                        }
                        fs.Flush();
                    }
                }
                barProgress.Value = 100;
                File.Delete(selectedFile);
                lblStatus2.Text = "File shredded and deleted";
                lblFile.Text = "No file selected";
                selectedFile = "";
                lblProgress.Text = "Done";
            }
            catch (Exception ex) { lblStatus2.Text = "Error: " + ex.Message; }
            finally { btnShred.Enabled = false; btnBrowse.Enabled = true; }
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblFile.Font.Dispose(); lblInfo.Font.Dispose(); lblProgress.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, lblInfo, barProgress, lblProgress, btnBrowse, btnShred, lblStatus2 });
        f.Show();
        SetStatus("File Shredder opened");
    }

    static void OpenPaletteGen()
    {
        var f = new Form();
        f.Text = "GM - Palette Generator";
        f.Size = new Size(650, 350);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblHex = new Label { Text = "Base Color (hex):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtHex = new TextBox { Font = new Font("Consolas", 11), Size = new Size(100, 28), Location = new Point(140, 9), Text = "#3366CC", BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle };
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGenerate = new Button { Text = "Generate", Location = new Point(250, 8), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGenerate.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "Click swatches to copy hex", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(350, 15) };

        Font swatchFont = new Font("Consolas", 8);
        Panel swatchPanel = new Panel { Location = new Point(10, 50), Size = new Size(620, 240), AutoScroll = true, BackColor = Color.FromArgb(20, 20, 35) };

        btnGenerate.Click += (s, e) =>
        {
            try
            {
                string hex = txtHex.Text.Trim().TrimStart('#');
                if (hex.Length != 6) { lblStatus2.Text = "Enter 6-char hex"; return; }
                int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                float h, sl, l;
                RgbToHsl(r, g, b, out h, out sl, out l);
                Color[] results = new Color[10];
                results[0] = Color.FromArgb(r, g, b);
                for (int i = 1; i < 10; i++)
                {
                    float newH = (h + i * 36) % 360f;
                    float newSl = sl;
                    float newL = l;
                    if (i % 3 == 0) { newL = Math.Min(1f, l + 0.15f); }
                    if (i % 3 == 1) { newSl = Math.Min(1f, sl + 0.1f); }
                    results[i] = HslToRgb(newH, newSl, newL);
                }
                foreach (Control c in swatchPanel.Controls) { foreach (Control cc in c.Controls) cc.Dispose(); c.Dispose(); }
                swatchPanel.Controls.Clear();
                for (int i = 0; i < 10; i++)
                {
                    Color c = results[i];
                    string cHex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                    var swatch = new Panel { Size = new Size(56, 70), Location = new Point(10 + (i * 61), 10), BackColor = c, BorderStyle = BorderStyle.FixedSingle };
                    var lblCode = new Label { Text = cHex, Font = swatchFont, ForeColor = Color.White, AutoSize = true, BackColor = Color.FromArgb(0, 0, 0), Location = new Point(0, 52), Dock = DockStyle.Bottom, Height = 18, TextAlign = ContentAlignment.MiddleCenter };
                    string copyHex = cHex;
                    swatch.Click += (ss, ee) => { try { Clipboard.SetText(copyHex); lblStatus2.Text = "Copied " + copyHex; } catch { } };
                    lblCode.Click += (ss, ee) => { try { Clipboard.SetText(copyHex); lblStatus2.Text = "Copied " + copyHex; } catch { } };
                    swatch.Controls.Add(lblCode);
                    swatchPanel.Controls.Add(swatch);
                }
                lblStatus2.Text = "Generated 10 palette colors";
            }
            catch { lblStatus2.Text = "Invalid hex color"; }
        };

        f.FormClosed += (s, e) => { foreach (Control c in swatchPanel.Controls) { foreach (Control cc in c.Controls) cc.Dispose(); c.Dispose(); } swatchPanel.Dispose(); btnFont.Dispose(); swatchFont.Dispose(); lblHex.Font.Dispose(); txtHex.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblHex, txtHex, btnGenerate, lblStatus2, swatchPanel });
        f.Show();
        SetStatus("Palette Generator opened");
    }

    static void RgbToHsl(int r, int g, int b, out float h, out float s, out float l)
    {
        float rf = r / 255f; float gf = g / 255f; float bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf)); float min = Math.Min(rf, Math.Min(gf, bf));
        l = (max + min) / 2f;
        if (max == min) { h = 0; s = 0; }
        else
        {
            float d = max - min;
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if (max == rf) h = (gf - bf) / d + (gf < bf ? 6f : 0f);
            else if (max == gf) h = (bf - rf) / d + 2f;
            else h = (rf - gf) / d + 4f;
            h *= 60f;
        }
    }

    static Color HslToRgb(float h, float s, float l)
    {
        if (s == 0) { int v = (int)(l * 255f); return Color.FromArgb(v, v, v); }
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hNorm = h / 360f;
        float r = HueToRgb(p, q, hNorm + 1f / 3f);
        float g = HueToRgb(p, q, hNorm);
        float bv = HueToRgb(p, q, hNorm - 1f / 3f);
        return Color.FromArgb((int)(r * 255f), (int)(g * 255f), (int)(bv * 255f));
    }

    static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    static void OpenMultiHash()
    {
        var f = new Form();
        f.Text = "GM - Multi-Hash Generator";
        f.Size = new Size(560, 330);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblFile = new Label { Text = "No file selected", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(520, 20), Location = new Point(10, 10) };
        var lblMd5 = new Label { Text = "MD5:    -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 40) };
        var lblSha1 = new Label { Text = "SHA1:   -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 65) };
        var lblSha256 = new Label { Text = "SHA256: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 90) };
        var lblSha384 = new Label { Text = "SHA384: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 115) };
        var lblSha512 = new Label { Text = "SHA512: -", Font = new Font("Consolas", 10), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 140) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnBrowse = new Button { Text = "Select File", Location = new Point(10, 175), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnCopyMd5 = new Button { Text = "Copy MD5", Location = new Point(120, 175), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyMd5.FlatAppearance.BorderSize = 0;
        var btnCopySha1 = new Button { Text = "Copy SHA1", Location = new Point(210, 175), Size = new Size(85, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha1.FlatAppearance.BorderSize = 0;
        var btnCopySha256 = new Button { Text = "Copy SHA256", Location = new Point(305, 175), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha256.FlatAppearance.BorderSize = 0;
        var btnCopySha384 = new Button { Text = "Copy SHA384", Location = new Point(120, 210), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha384.FlatAppearance.BorderSize = 0;
        var btnCopySha512 = new Button { Text = "Copy SHA512", Location = new Point(230, 210), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopySha512.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 250) };

        string lastMd5 = "", lastSha1 = "", lastSha256 = "", lastSha384 = "", lastSha512 = "";

        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File for Multi-Hash", "Any file");
            if (path != null)
            {
                try
                {
                    lblFile.Text = Path.GetFileName(path);
                    lblStatus2.Text = "Calculating all hashes...";
                    using (var fs = File.OpenRead(path))
                    {
                        using (var md5 = MD5.Create()) { byte[] h = md5.ComputeHash(fs); lastMd5 = BitConverter.ToString(h).Replace("-", "").ToLower(); lblMd5.Text = "MD5:    " + lastMd5; }
                        fs.Position = 0;
                        using (var sha1 = System.Security.Cryptography.SHA1.Create()) { byte[] h = sha1.ComputeHash(fs); lastSha1 = BitConverter.ToString(h).Replace("-", "").ToLower(); lblSha1.Text = "SHA1:   " + lastSha1; }
                        fs.Position = 0;
                        using (var sha256 = System.Security.Cryptography.SHA256.Create()) { byte[] h = sha256.ComputeHash(fs); lastSha256 = BitConverter.ToString(h).Replace("-", "").ToLower(); lblSha256.Text = "SHA256: " + lastSha256; }
                        fs.Position = 0;
                        using (var sha384 = System.Security.Cryptography.SHA384.Create()) { byte[] h = sha384.ComputeHash(fs); lastSha384 = BitConverter.ToString(h).Replace("-", "").ToLower(); lblSha384.Text = "SHA384: " + lastSha384; }
                        fs.Position = 0;
                        using (var sha512 = System.Security.Cryptography.SHA512.Create()) { byte[] h = sha512.ComputeHash(fs); lastSha512 = BitConverter.ToString(h).Replace("-", "").ToLower(); lblSha512.Text = "SHA512: " + lastSha512; }
                    }
                    lblStatus2.Text = "All 5 hashes calculated";
                }
                catch { lblStatus2.Text = "Error calculating hashes"; }
            }
        };
        btnCopyMd5.Click += (s, e) => { try { if (lastMd5.Length > 0) { Clipboard.SetText(lastMd5); lblStatus2.Text = "MD5 copied"; } } catch { } };
        btnCopySha1.Click += (s, e) => { try { if (lastSha1.Length > 0) { Clipboard.SetText(lastSha1); lblStatus2.Text = "SHA1 copied"; } } catch { } };
        btnCopySha256.Click += (s, e) => { try { if (lastSha256.Length > 0) { Clipboard.SetText(lastSha256); lblStatus2.Text = "SHA256 copied"; } } catch { } };
        btnCopySha384.Click += (s, e) => { try { if (lastSha384.Length > 0) { Clipboard.SetText(lastSha384); lblStatus2.Text = "SHA384 copied"; } } catch { } };
        btnCopySha512.Click += (s, e) => { try { if (lastSha512.Length > 0) { Clipboard.SetText(lastSha512); lblStatus2.Text = "SHA512 copied"; } } catch { } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblFile.Font.Dispose(); lblMd5.Font.Dispose(); lblSha1.Font.Dispose(); lblSha256.Font.Dispose(); lblSha384.Font.Dispose(); lblSha512.Font.Dispose(); btnCopyMd5.Font.Dispose(); btnCopySha1.Font.Dispose(); btnCopySha256.Font.Dispose(); btnCopySha384.Font.Dispose(); btnCopySha512.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, lblMd5, lblSha1, lblSha256, lblSha384, lblSha512, btnBrowse, btnCopyMd5, btnCopySha1, btnCopySha256, btnCopySha384, btnCopySha512, lblStatus2 });
        f.Show();
        SetStatus("Multi-Hash Generator opened");
    }

    static void OpenWhois()
    {
        var f = new Form();
        f.Text = "GM - WHOIS / DNS Lookup";
        f.Size = new Size(500, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblDomain = new Label { Text = "Domain:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtDomain = new TextBox { Font = new Font("Consolas", 11), Size = new Size(250, 28), Location = new Point(75, 9), Text = "example.com", BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(460, 280), Location = new Point(10, 50), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.FromArgb(0, 200, 100), BorderStyle = BorderStyle.None, ReadOnly = true };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnLookup = new Button { Text = "Lookup", Location = new Point(335, 8), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLookup.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(10, 345), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(100, 352) };

        btnLookup.Click += (s, e) =>
        {
            string domain = txtDomain.Text.Trim().ToLower().Replace("http://", "").Replace("https://", "");
            if (domain.Length == 0) { lblStatus2.Text = "Enter a domain"; return; }
            int slash = domain.IndexOf('/');
            if (slash > 0) domain = domain.Substring(0, slash);
            try
            {
                txtOutput.Text = "Looking up " + domain + "...\n";
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== WHOIS / DNS Lookup ===");
                sb.AppendLine("Domain: " + domain);
                sb.AppendLine("");
                try
                {
                    var addresses = System.Net.Dns.GetHostAddresses(domain);
                    sb.AppendLine("--- IP Addresses ---");
                    foreach (var addr in addresses)
                    {
                        sb.AppendLine("  " + addr.ToString());
                    }
                }
                catch (Exception ex) { sb.AppendLine("DNS lookup failed: " + ex.Message); }
                try
                {
                    var entry = System.Net.Dns.GetHostEntry(domain);
                    sb.AppendLine("");
                    sb.AppendLine("--- Host Entry ---");
                    sb.AppendLine("  Hostname: " + entry.HostName);
                    if (entry.Aliases.Length > 0)
                    {
                        sb.AppendLine("  Aliases:");
                        foreach (string alias in entry.Aliases)
                        {
                            sb.AppendLine("    " + alias);
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine("Host entry failed: " + ex.Message); }
                sb.AppendLine("");
                sb.AppendLine("--- Additional Info ---");
                sb.AppendLine("  Check https://who.is/whois/" + domain);
                sb.AppendLine("  Check https://www.nslookup.io/domains/" + domain);
                txtOutput.Text = sb.ToString();
                lblStatus2.Text = "Lookup complete";
            }
            catch { txtOutput.Text = "Lookup failed"; lblStatus2.Text = "Error"; }
        };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus2.Text = "Copied"; } } catch { } };
        txtDomain.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { btnLookup.PerformClick(); e.SuppressKeyPress = true; } };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblDomain.Font.Dispose(); txtDomain.Font.Dispose(); txtOutput.Font.Dispose(); btnCopy.Font.Dispose(); lblStatus2.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblDomain, txtDomain, txtOutput, btnLookup, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("WHOIS Lookup opened");
    }

    static void OpenBarcodeGen()
    {
        var f = new Form();
        f.Text = "GM - Barcode Generator";
        f.Size = new Size(550, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Text:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtInput = new TextBox { Font = new Font("Consolas", 11), Size = new Size(300, 28), Location = new Point(55, 9), Text = "Hello World", BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var picBarcode = new PictureBox { Size = new Size(510, 200), Location = new Point(10, 45), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.CenterImage };
        var lblText = new Label { Text = "Code128-style barcode", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = true, Location = new Point(10, 250) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGenerate = new Button { Text = "Generate", Location = new Point(365, 8), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGenerate.FlatAppearance.BorderSize = 0;
        var btnSave = new Button { Text = "Save", Location = new Point(10, 280), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(100, 280), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(190, 287) };

        Bitmap currentBarcode = null;

        Action generateBarcode = () =>
        {
            string text = txtInput.Text;
            if (text.Length == 0) { lblStatus2.Text = "Enter text"; return; }
            try
            {
                int barWidth = 2;
                int totalWidth = text.Length * 8 * barWidth + 40;
                int height = 150;
                if (currentBarcode != null) currentBarcode.Dispose();
                currentBarcode = new Bitmap(totalWidth, height);
                using (var g = Graphics.FromImage(currentBarcode))
                {
                    g.Clear(Color.White);
                    using (var barBrush = new SolidBrush(Color.Black))
                    {
                        int x = 20;
                        for (int i = 0; i < text.Length; i++)
                        {
                            int charVal = (int)text[i];
                            for (int bit = 6; bit >= 0; bit--)
                            {
                                bool isBar = ((charVal >> bit) & 1) == 1;
                                int w = (i % 3 == 0) ? barWidth + 1 : barWidth;
                                if (isBar)
                                {
                                    g.FillRectangle(barBrush, x, 10, w, height - 30);
                                }
                                x += w;
                            }
                            x += 1;
                        }
                    }
                    using (var font = new Font("Consolas", 10))
                    {
                        var textSize = g.MeasureString(text, font);
                        float textX = (totalWidth - textSize.Width) / 2f;
                        g.DrawString(text, font, Brushes.Black, textX, height - 20);
                    }
                }
                picBarcode.Image = currentBarcode;
                lblStatus2.Text = "Barcode generated for \"" + text + "\"";
            }
            catch { lblStatus2.Text = "Generation error"; }
        };

        btnGenerate.Click += (s, e) => generateBarcode();
        txtInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { generateBarcode(); e.SuppressKeyPress = true; } };
        btnSave.Click += (s, e) =>
        {
            if (currentBarcode == null) { lblStatus2.Text = "Generate first"; return; }
            string path = PromptSavePath("GM - Save Barcode", "PNG image", "barcode_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            if (path != null)
            {
                try { currentBarcode.Save(path, System.Drawing.Imaging.ImageFormat.Png); lblStatus2.Text = "Saved to " + Path.GetFileName(path); }
                catch { lblStatus2.Text = "Save error"; }
            }
        };
        btnCopy.Click += (s, e) =>
        {
            if (currentBarcode == null) { lblStatus2.Text = "Generate first"; return; }
            try { Clipboard.SetImage(currentBarcode); lblStatus2.Text = "Copied to clipboard"; }
            catch { lblStatus2.Text = "Copy error"; }
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblText.Font.Dispose(); lblStatus2.Font.Dispose(); if (currentBarcode != null) currentBarcode.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, picBarcode, lblText, btnGenerate, btnSave, btnCopy, lblStatus2 });
        f.Show();
        SetStatus("Barcode Generator opened");
    }

    static void OpenColorHarmony()
    {
        var f = new Form();
        f.Text = "GM - Color Harmony Wheel";
        f.Size = new Size(600, 500);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblHex = new Label { Text = "Base Color (hex):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtHex = new TextBox { Font = new Font("Consolas", 11), Size = new Size(100, 28), Location = new Point(140, 9), Text = "#FF6600", BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle };
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGenerate = new Button { Text = "Generate", Location = new Point(250, 8), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGenerate.FlatAppearance.BorderSize = 0;
        var lblStatus2 = new Label { Text = "Click swatches to copy hex", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(350, 15) };

        var picWheel = new PictureBox { Size = new Size(250, 250), Location = new Point(10, 50), BackColor = Color.Transparent };

        Font labelFont = new Font("Segoe UI", 8, FontStyle.Bold);
        Font hexFont = new Font("Consolas", 8);
        Panel harmonyPanel = new Panel { Location = new Point(270, 50), Size = new Size(310, 400), AutoScroll = true, BackColor = Color.FromArgb(20, 20, 35) };

        btnGenerate.Click += (s, e) =>
        {
            try
            {
                string hex = txtHex.Text.Trim().TrimStart('#');
                if (hex.Length != 6) { lblStatus2.Text = "Enter 6-char hex"; return; }
                int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                float baseH, baseS, baseL;
                RgbToHsl(r, g, b, out baseH, out baseS, out baseL);

                Bitmap wheel = new Bitmap(250, 250);
                using (var g2 = Graphics.FromImage(wheel))
                {
                    g2.Clear(Color.FromArgb(15, 15, 25));
                    int cx = 125, cy = 125, radius = 100;
                    for (int angle = 0; angle < 360; angle++)
                    {
                        using (var pen = new Pen(HslToRgb(angle, baseS, baseL), 3))
                        {
                            double rad = angle * Math.PI / 180.0;
                            int x1 = cx + (int)(Math.Cos(rad) * (radius - 15));
                            int y1 = cy + (int)(Math.Sin(rad) * (radius - 15));
                            int x2 = cx + (int)(Math.Cos(rad) * radius);
                            int y2 = cy + (int)(Math.Sin(rad) * radius);
                            g2.DrawLine(pen, x1, y1, x2, y2);
                        }
                    }
                    double baseRad = baseH * Math.PI / 180.0;
                    int bx = cx + (int)(Math.Cos(baseRad) * radius);
                    int by = cy + (int)(Math.Sin(baseRad) * radius);
                    using (var markerBrush = new SolidBrush(Color.White))
                    {
                        g2.FillEllipse(markerBrush, bx - 5, by - 5, 10, 10);
                    }
                    g2.DrawEllipse(Pens.White, cx - radius - 5, cy - radius - 5, radius * 2 + 10, radius * 2 + 10);
                }
                if (picWheel.Image != null) picWheel.Image.Dispose();
                picWheel.Image = wheel;

                foreach (Control c in harmonyPanel.Controls) { foreach (Control cc in c.Controls) cc.Dispose(); c.Dispose(); }
                harmonyPanel.Controls.Clear();
                int yPos = 5;
                Action<string, float[]> addHarmony = (name, hues) =>
                {
                    var lblName = new Label { Text = name, Font = labelFont, ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(5, yPos) };
                    harmonyPanel.Controls.Add(lblName);
                    yPos += 22;
                    int sx = 5;
                    for (int i = 0; i < hues.Length; i++)
                    {
                        float nh = ((hues[i] % 360f) + 360f) % 360f;
                        Color nc = HslToRgb(nh, baseS, baseL);
                        string ncHex = "#" + nc.R.ToString("X2") + nc.G.ToString("X2") + nc.B.ToString("X2");
                        var swatch = new Panel { Size = new Size(55, 45), Location = new Point(sx, yPos), BackColor = nc, BorderStyle = BorderStyle.FixedSingle };
                        var codeLabel = new Label { Text = ncHex, Font = hexFont, ForeColor = Color.White, BackColor = Color.FromArgb(0, 0, 0), Dock = DockStyle.Bottom, Height = 16, TextAlign = ContentAlignment.MiddleCenter };
                        string copyVal = ncHex;
                        swatch.Click += (ss, ee) => { try { Clipboard.SetText(copyVal); lblStatus2.Text = "Copied " + copyVal; } catch { } };
                        codeLabel.Click += (ss, ee) => { try { Clipboard.SetText(copyVal); lblStatus2.Text = "Copied " + copyVal; } catch { } };
                        swatch.Controls.Add(codeLabel);
                        harmonyPanel.Controls.Add(swatch);
                        sx += 62;
                    }
                    yPos += 55;
                };

                addHarmony("Complementary", new float[] { baseH, (baseH + 180f) % 360f });
                addHarmony("Split-Complementary", new float[] { baseH, (baseH + 150f) % 360f, (baseH + 210f) % 360f });
                addHarmony("Triadic", new float[] { baseH, (baseH + 120f) % 360f, (baseH + 240f) % 360f });
                addHarmony("Tetradic", new float[] { baseH, (baseH + 90f) % 360f, (baseH + 180f) % 360f, (baseH + 270f) % 360f });
                addHarmony("Analogous", new float[] { (baseH - 30f + 360f) % 360f, baseH, (baseH + 30f) % 360f, (baseH + 60f) % 360f });

                lblStatus2.Text = "Harmonies generated";
            }
            catch { lblStatus2.Text = "Invalid hex color"; }
        };

        f.FormClosed += (s, e) => { foreach (Control c in harmonyPanel.Controls) { foreach (Control cc in c.Controls) cc.Dispose(); c.Dispose(); } harmonyPanel.Dispose(); btnFont.Dispose(); labelFont.Dispose(); hexFont.Dispose(); lblHex.Font.Dispose(); txtHex.Font.Dispose(); lblStatus2.Font.Dispose(); if (picWheel.Image != null) picWheel.Image.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblHex, txtHex, btnGenerate, lblStatus2, picWheel, harmonyPanel });
        f.Show();
        SetStatus("Color Harmony Wheel opened");
    }






    static void OpenScreenColorPicker()
    {
        var f = new Form();
        f.Text = "GM - Screen Color Picker";
        f.Size = new Size(320, 260);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblPreview = new Label { Size = new Size(280, 50), Location = new Point(10, 10), BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
        var lblHex = new Label { Text = "Hex: #000000", Font = monoFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 70) };
        var lblRgb = new Label { Text = "RGB: 0, 0, 0", Font = monoFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 95) };

        var btnPick = new Button { Text = "Pick Color", Location = new Point(10, 125), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnPick.FlatAppearance.BorderSize = 0;
        var btnCopyHex = new Button { Text = "Copy Hex", Location = new Point(150, 125), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopyHex.FlatAppearance.BorderSize = 0;

        string lastHex = "#000000";
        string lastRgb = "0, 0, 0";

        var poll = new System.Windows.Forms.Timer();
        poll.Interval = 100;
        poll.Tick += (s, e) =>
        {
            Point p;
            GetCursorPos(out p);
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;
            uint pixel = GetPixel(hdc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, hdc);
            Color c = Color.FromArgb((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF));
            lblPreview.BackColor = c;
            lastHex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            lastRgb = c.R + ", " + c.G + ", " + c.B;
            lblHex.Text = "Hex: " + lastHex;
            lblRgb.Text = "RGB: " + lastRgb;
        };
        poll.Start();

        btnPick.Click += (s, e) =>
        {
            Point p;
            GetCursorPos(out p);
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;
            uint pixel = GetPixel(hdc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, hdc);
            Color c = Color.FromArgb((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF));
            lblPreview.BackColor = c;
            lastHex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            lastRgb = c.R + ", " + c.G + ", " + c.B;
            lblHex.Text = "Hex: " + lastHex;
            lblRgb.Text = "RGB: " + lastRgb;
        };
        btnCopyHex.Click += (s, e) => { try { Clipboard.SetText(lastHex); } catch { } };

        f.FormClosed += (s, e) => { poll.Stop(); poll.Dispose(); lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblPreview, lblHex, lblRgb, btnPick, btnCopyHex });
        f.Show();
        SetStatus("Screen Color Picker opened");
    }

    static void OpenImageResizer()
    {
        var f = new Form();
        f.Text = "GM - Image Resizer";
        f.Size = new Size(400, 280);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblFile = new Label { Text = "No file selected", Font = lblFont, ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(370, 20), Location = new Point(10, 10) };
        var lblW = new Label { Text = "Width:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 40) };
        var txtWidth = new TextBox { Font = monoFont, Size = new Size(100, 24), Location = new Point(70, 37), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblH = new Label { Text = "Height:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(180, 40) };
        var txtHeight = new TextBox { Font = monoFont, Size = new Size(100, 24), Location = new Point(240, 37), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var chkLock = new CheckBox { Text = "Lock aspect ratio", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 72), Checked = true };
        var btnBrowse = new Button { Text = "Browse", Location = new Point(10, 105), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnResize = new Button { Text = "Resize & Save", Location = new Point(110, 105), Size = new Size(120, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnResize.FlatAppearance.BorderSize = 0;
        var lblStatus = new Label { Text = "", Font = lblFont, ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(10, 150) };

        string srcPath = "";
        double aspect = 1.0;
        int origW = 1, origH = 1;

        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select Image", "Image files");
            if (path != null)
            {
                srcPath = path;
                lblFile.Text = Path.GetFileName(path);
                try
                {
                    using (var bmp = new Bitmap(path))
                    {
                        origW = bmp.Width;
                        origH = bmp.Height;
                        aspect = (double)origW / origH;
                        txtWidth.Text = origW.ToString();
                        txtHeight.Text = origH.ToString();
                    }
                    lblStatus.Text = "Loaded: " + origW + "x" + origH;
                }
                catch { lblStatus.Text = "Error loading image"; }
            }
        };

        txtWidth.TextChanged += (s, e) =>
        {
            if (chkLock.Checked)
            {
                int w;
                if (int.TryParse(txtWidth.Text, out w) && aspect > 0)
                    txtHeight.Text = ((int)(w / aspect)).ToString();
            }
        };
        txtHeight.TextChanged += (s, e) =>
        {
            if (chkLock.Checked)
            {
                int h;
                if (int.TryParse(txtHeight.Text, out h) && aspect > 0)
                    txtWidth.Text = ((int)(h * aspect)).ToString();
            }
        };

        btnResize.Click += (s, e) =>
        {
            if (srcPath.Length == 0) { lblStatus.Text = "Select a source image first"; return; }
            int w, h;
            if (!int.TryParse(txtWidth.Text, out w) || !int.TryParse(txtHeight.Text, out h) || w <= 0 || h <= 0) { lblStatus.Text = "Invalid dimensions"; return; }
            string savePath = PromptSavePath("GM - Save Resized Image", "PNG files", "resized.png");
            if (savePath == null) return;
            try
            {
                using (var src = new Bitmap(srcPath))
                using (var dst = new Bitmap(w, h))
                using (var g = Graphics.FromImage(dst))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                    dst.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
                }
                lblStatus.Text = "Saved: " + w + "x" + h;
            }
            catch (Exception ex) { lblStatus.Text = "Error: " + ex.Message; }
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, lblW, txtWidth, lblH, txtHeight, chkLock, btnBrowse, btnResize, lblStatus });
        f.Show();
        SetStatus("Image Resizer opened");
    }

    static void OpenUnitConverter()
    {
        var f = new Form();
        f.Text = "GM - Unit Converter";
        f.Size = new Size(420, 300);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        Font resFont = new Font("Consolas", 12, FontStyle.Bold);

        var lblCat = new Label { Text = "Category:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 15) };
        var cmbCat = new ComboBox { Font = lblFont, Size = new Size(150, 24), Location = new Point(80, 12), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };

        var lblFrom = new Label { Text = "From:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 55) };
        var cmbFrom = new ComboBox { Font = lblFont, Size = new Size(120, 24), Location = new Point(60, 52), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };
        var lblTo = new Label { Text = "To:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(200, 55) };
        var cmbTo = new ComboBox { Font = lblFont, Size = new Size(120, 24), Location = new Point(230, 52), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };

        var lblVal = new Label { Text = "Value:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 95) };
        var txtValue = new TextBox { Font = monoFont, Size = new Size(150, 24), Location = new Point(60, 92), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var btnConvert = new Button { Text = "Convert", Location = new Point(230, 90), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnConvert.FlatAppearance.BorderSize = 0;

        var lblResult = new Label { Text = "Result: -", Font = resFont, ForeColor = Color.FromArgb(0, 200, 100), AutoSize = true, Location = new Point(10, 140) };

        string[] lenUnits = { "mm", "cm", "m", "km", "in", "ft", "yd", "mi" };
        double[] lenFactors = { 0.001, 0.01, 1.0, 1000.0, 0.0254, 0.3048, 0.9144, 1609.344 };
        string[] wUnits = { "mg", "g", "kg", "lb", "oz" };
        double[] wFactors = { 0.000001, 0.001, 1.0, 0.453592, 0.0283495 };
        string[] tUnits = { "C", "F", "K" };
        string[] sUnits = { "m/s", "km/h", "mph", "knots" };
        double[] sFactors = { 1.0, 0.277778, 0.44704, 0.514444 };

        cmbCat.Items.AddRange(new object[] { "Length", "Weight", "Temperature", "Speed" });
        cmbCat.SelectedIndex = 0;

        cmbCat.SelectedIndexChanged += (s, e) =>
        {
            cmbFrom.Items.Clear();
            cmbTo.Items.Clear();
            int idx = cmbCat.SelectedIndex;
            string[] units = null;
            if (idx == 0) units = lenUnits;
            else if (idx == 1) units = wUnits;
            else if (idx == 2) units = tUnits;
            else if (idx == 3) units = sUnits;
            if (units == null) return;
            cmbFrom.Items.AddRange(units);
            cmbTo.Items.AddRange(units);
            if (cmbFrom.Items.Count > 0) cmbFrom.SelectedIndex = 0;
            if (cmbTo.Items.Count > 1) cmbTo.SelectedIndex = 1;
            else if (cmbTo.Items.Count > 0) cmbTo.SelectedIndex = 0;
        };
        cmbCat.SelectedIndex = 0;

        btnConvert.Click += (s, e) =>
        {
            double val;
            if (!double.TryParse(txtValue.Text, out val)) { lblResult.Text = "Result: Invalid input"; return; }
            string fromUnit = cmbFrom.SelectedItem != null ? cmbFrom.SelectedItem.ToString() : "";
            string toUnit = cmbTo.SelectedItem != null ? cmbTo.SelectedItem.ToString() : "";
            int catIdx = cmbCat.SelectedIndex;
            double result = 0;

            if (catIdx == 0)
            {
                int fi = Array.IndexOf(lenUnits, fromUnit);
                int ti = Array.IndexOf(lenUnits, toUnit);
                if (fi >= 0 && ti >= 0) result = val * lenFactors[fi] / lenFactors[ti];
            }
            else if (catIdx == 1)
            {
                int fi = Array.IndexOf(wUnits, fromUnit);
                int ti = Array.IndexOf(wUnits, toUnit);
                if (fi >= 0 && ti >= 0) result = val * wFactors[fi] / wFactors[ti];
            }
            else if (catIdx == 2)
            {
                double celsius = val;
                if (fromUnit == "F") celsius = (val - 32.0) * 5.0 / 9.0;
                else if (fromUnit == "K") celsius = val - 273.15;
                if (toUnit == "C") result = celsius;
                else if (toUnit == "F") result = celsius * 9.0 / 5.0 + 32.0;
                else if (toUnit == "K") result = celsius + 273.15;
            }
            else if (catIdx == 3)
            {
                int fi = Array.IndexOf(sUnits, fromUnit);
                int ti = Array.IndexOf(sUnits, toUnit);
                if (fi >= 0 && ti >= 0) result = val * sFactors[fi] / sFactors[ti];
            }
            lblResult.Text = "Result: " + result.ToString("G");
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); resFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblCat, cmbCat, lblFrom, cmbFrom, lblTo, cmbTo, lblVal, txtValue, btnConvert, lblResult });
        f.Show();
        SetStatus("Unit Converter opened");
    }

    static void OpenBaseConverter()
    {
        var f = new Form();
        f.Text = "GM - Base Converter";
        f.Size = new Size(450, 280);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblInput = new Label { Text = "Input:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 15) };
        var txtInput = new TextBox { Font = monoFont, Size = new Size(400, 24), Location = new Point(10, 38), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

        var lblDec = new Label { Text = "Decimal:", Font = lblFont, ForeColor = Color.White, AutoSize = false, Size = new Size(320, 20), Location = new Point(10, 72) };
        var btnDec = new Button { Text = "Copy", Location = new Point(340, 70), Size = new Size(70, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDec.FlatAppearance.BorderSize = 0;

        var lblBin = new Label { Text = "Binary:", Font = lblFont, ForeColor = Color.White, AutoSize = false, Size = new Size(320, 20), Location = new Point(10, 100) };
        var btnBin = new Button { Text = "Copy", Location = new Point(340, 98), Size = new Size(70, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBin.FlatAppearance.BorderSize = 0;

        var lblOct = new Label { Text = "Octal:", Font = lblFont, ForeColor = Color.White, AutoSize = false, Size = new Size(320, 20), Location = new Point(10, 128) };
        var btnOct = new Button { Text = "Copy", Location = new Point(340, 126), Size = new Size(70, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnOct.FlatAppearance.BorderSize = 0;

        var lblHex = new Label { Text = "Hex:", Font = lblFont, ForeColor = Color.White, AutoSize = false, Size = new Size(320, 20), Location = new Point(10, 156) };
        var btnHex = new Button { Text = "Copy", Location = new Point(340, 154), Size = new Size(70, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnHex.FlatAppearance.BorderSize = 0;

        string lastDec = "", lastBin = "", lastOct = "", lastHexVal = "";

        txtInput.TextChanged += (s, e) =>
        {
            long val;
            if (!long.TryParse(txtInput.Text, out val))
            {
                lblDec.Text = "Decimal: -";
                lblBin.Text = "Binary: -";
                lblOct.Text = "Octal: -";
                lblHex.Text = "Hex: -";
                lastDec = ""; lastBin = ""; lastOct = ""; lastHexVal = "";
                return;
            }
            lastDec = val.ToString();
            lastBin = Convert.ToString(val, 2);
            lastOct = Convert.ToString(val, 8);
            lastHexVal = Convert.ToString(val, 16).ToUpper();
            lblDec.Text = "Decimal: " + lastDec;
            lblBin.Text = "Binary:  " + lastBin;
            lblOct.Text = "Octal:   " + lastOct;
            lblHex.Text = "Hex:     " + lastHexVal;
        };

        btnDec.Click += (s, e) => { try { if (lastDec.Length > 0) Clipboard.SetText(lastDec); } catch { } };
        btnBin.Click += (s, e) => { try { if (lastBin.Length > 0) Clipboard.SetText(lastBin); } catch { } };
        btnOct.Click += (s, e) => { try { if (lastOct.Length > 0) Clipboard.SetText(lastOct); } catch { } };
        btnHex.Click += (s, e) => { try { if (lastHexVal.Length > 0) Clipboard.SetText(lastHexVal); } catch { } };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblDec, btnDec, lblBin, btnBin, lblOct, btnOct, lblHex, btnHex });
        f.Show();
        SetStatus("Base Converter opened");
    }

    static void OpenTextReplacer()
    {
        var f = new Form();
        f.Text = "GM - Text Replacer";
        f.Size = new Size(550, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var rules = new Dictionary<string, string>();
        rules["brh"] = "Best regards,\nHenry";
        rules["ty"] = "Thank you";
        rules["np"] = "No problem";
        rules["omw"] = "On my way";

        var listRules = new ListBox { Font = monoFont, Size = new Size(250, 150), Location = new Point(10, 10), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.FromArgb(0, 200, 100) };

        var lblShortcut = new Label { Text = "Shortcut:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(270, 12) };
        var txtShortcut = new TextBox { Font = monoFont, Size = new Size(120, 24), Location = new Point(270, 32), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblExpansion = new Label { Text = "Expansion:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(270, 62) };
        var txtExpansion = new TextBox { Font = monoFont, Size = new Size(120, 24), Location = new Point(270, 82), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var btnAdd = new Button { Text = "Add Rule", Location = new Point(270, 115), Size = new Size(120, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnAdd.FlatAppearance.BorderSize = 0;

        var lblInput = new Label { Text = "Input:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 170) };
        var txtInput = new TextBox { Font = monoFont, Size = new Size(520, 60), Location = new Point(10, 190), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Multiline = true };
        var btnReplace = new Button { Text = "Replace All", Location = new Point(10, 256), Size = new Size(120, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnReplace.FlatAppearance.BorderSize = 0;
        var lblOutput = new Label { Text = "Output:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 296) };
        var txtOutput = new TextBox { Font = monoFont, Size = new Size(520, 60), Location = new Point(10, 316), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Multiline = true, ReadOnly = true };

        Action refreshList = null;
        refreshList = delegate()
        {
            listRules.Items.Clear();
            foreach (var kv in rules)
                listRules.Items.Add("\"" + kv.Key + "\" -> \"" + kv.Value.Replace("\n", "\\n") + "\"");
        };
        refreshList();

        btnAdd.Click += (s, e) =>
        {
            string sc = txtShortcut.Text.Trim();
            string ex = txtExpansion.Text.Trim();
            if (sc.Length == 0 || ex.Length == 0) return;
            rules[sc] = ex;
            txtShortcut.Text = "";
            txtExpansion.Text = "";
            refreshList();
        };

        btnReplace.Click += (s, e) =>
        {
            string input = txtInput.Text;
            foreach (var kv in rules)
                input = System.Text.RegularExpressions.Regex.Replace(input, @"\b" + System.Text.RegularExpressions.Regex.Escape(kv.Key) + @"\b", kv.Value);
            txtOutput.Text = input;
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { listRules, lblShortcut, txtShortcut, lblExpansion, txtExpansion, btnAdd, lblInput, txtInput, btnReplace, lblOutput, txtOutput });
        f.Show();
        SetStatus("Text Replacer opened");
    }

    static void OpenFileEncrypt()
    {
        var f = new Form();
        f.Text = "GM - File Encrypt/Decrypt";
        f.Size = new Size(450, 220);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblFile = new Label { Text = "No file selected", Font = lblFont, ForeColor = Color.FromArgb(120, 120, 140), AutoSize = false, Size = new Size(420, 20), Location = new Point(10, 10) };
        var lblPass = new Label { Text = "Password:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 40) };
        var txtPass = new TextBox { Font = monoFont, Size = new Size(300, 24), Location = new Point(90, 37), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };

        var btnBrowse = new Button { Text = "Browse", Location = new Point(10, 75), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnBrowse.FlatAppearance.BorderSize = 0;
        var btnEncrypt = new Button { Text = "Encrypt", Location = new Point(110, 75), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnEncrypt.FlatAppearance.BorderSize = 0;
        var btnDecrypt = new Button { Text = "Decrypt", Location = new Point(220, 75), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(180, 100, 0), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnDecrypt.FlatAppearance.BorderSize = 0;

        var lblStatus = new Label { Text = "", Font = lblFont, ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(10, 120) };

        string srcPath = "";

        btnBrowse.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select File", "Any file");
            if (path != null) { srcPath = path; lblFile.Text = Path.GetFileName(path); }
        };

        btnEncrypt.Click += (s, e) =>
        {
            if (srcPath.Length == 0) { lblStatus.Text = "Select a file first"; return; }
            if (txtPass.Text.Length == 0) { lblStatus.Text = "Enter a password"; return; }
            try
            {
                string savePath = PromptSavePath("GM - Save Encrypted File", "All files", Path.GetFileName(srcPath) + ".enc");
                if (savePath == null) return;
                byte[] salt = new byte[16];
                using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);
                byte[] key;
                using (var derive = new Rfc2898DeriveBytes(txtPass.Text, salt, 10000))
                    key = derive.GetBytes(32);
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.GenerateIV();
                    using (var fsOut = File.Create(savePath))
                    {
                        fsOut.Write(salt, 0, salt.Length);
                        fsOut.Write(aes.IV, 0, aes.IV.Length);
                        using (var cs = new CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        using (var fsIn = File.OpenRead(srcPath))
                            fsIn.CopyTo(cs);
                    }
                }
                lblStatus.Text = "Encrypted: " + Path.GetFileName(savePath);
            }
            catch (Exception ex) { lblStatus.Text = "Error: " + ex.Message; }
        };

        btnDecrypt.Click += (s, e) =>
        {
            if (srcPath.Length == 0) { lblStatus.Text = "Select a file first"; return; }
            if (txtPass.Text.Length == 0) { lblStatus.Text = "Enter a password"; return; }
            try
            {
                string savePath = PromptSavePath("GM - Save Decrypted File", "All files", "decrypted_" + Path.GetFileName(srcPath).Replace(".enc", ""));
                if (savePath == null) return;
                using (var fsIn = File.OpenRead(srcPath))
                {
                    byte[] salt = new byte[16];
                    int saltRead = 0;
                    while (saltRead < 16) { int n = fsIn.Read(salt, saltRead, 16 - saltRead); if (n == 0) throw new Exception("File too short"); saltRead += n; }
                    byte[] iv = new byte[16];
                    int ivRead = 0;
                    while (ivRead < 16) { int n = fsIn.Read(iv, ivRead, 16 - ivRead); if (n == 0) throw new Exception("File too short"); ivRead += n; }
                    byte[] key;
                    using (var derive = new Rfc2898DeriveBytes(txtPass.Text, salt, 10000))
                        key = derive.GetBytes(32);
                    using (var aes = Aes.Create())
                    {
                        aes.Key = key;
                        aes.IV = iv;
                        using (var cs = new CryptoStream(fsIn, aes.CreateDecryptor(), CryptoStreamMode.Read))
                        using (var fsOut = File.Create(savePath))
                            cs.CopyTo(fsOut);
                    }
                }
                lblStatus.Text = "Decrypted: " + Path.GetFileName(savePath);
            }
            catch { lblStatus.Text = "Decryption failed - wrong password or corrupted file"; }
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblFile, lblPass, txtPass, btnBrowse, btnEncrypt, btnDecrypt, lblStatus });
        f.Show();
        SetStatus("File Encrypt/Decrypt opened");
    }

    static void OpenDiskAnalyzer()
    {
        var f = new Form();
        f.Text = "GM - Disk Analyzer";
        f.Size = new Size(550, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font monoFont = new Font("Consolas", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        var lblDrive = new Label { Text = "Drive:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 15) };
        var cmbDrive = new ComboBox { Font = lblFont, Size = new Size(150, 24), Location = new Point(60, 12), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White };
        var btnScan = new Button { Text = "Scan", Location = new Point(230, 10), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnScan.FlatAppearance.BorderSize = 0;

        var listView = new ListView { Font = monoFont, Size = new Size(520, 250), Location = new Point(10, 45), View = View.Details, BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.White, FullRowSelect = true };
        listView.Columns.Add("File", 360);
        listView.Columns.Add("Size", 140);

        var lblInfo = new Label { Text = "", Font = lblFont, ForeColor = Color.FromArgb(0, 200, 100), AutoSize = true, Location = new Point(10, 305) };

        foreach (DriveInfo di in DriveInfo.GetDrives())
        {
            if (di.IsReady) cmbDrive.Items.Add(di.Name);
        }
        if (cmbDrive.Items.Count > 0) cmbDrive.SelectedIndex = 0;

        btnScan.Click += (s, e) =>
        {
            if (cmbDrive.SelectedItem == null) return;
            string drive = cmbDrive.SelectedItem.ToString();
            btnScan.Enabled = false;
            btnScan.Text = "Scanning...";
            listView.Items.Clear();
            Application.DoEvents();

            var files = new List<KeyValuePair<string, long>>();
            long totalSize = 0;
            int totalCount = 0;
            try
            {
                string[] extensions = { ".exe", ".dll", ".zip", ".rar", ".mp4", ".mp3", ".avi", ".mkv", ".iso", ".log", ".tmp", ".bak", ".dat", ".pdb" };
                foreach (string ext in extensions)
                {
                    try
                    {
                        foreach (string file in Directory.GetFiles(drive, "*" + ext, SearchOption.AllDirectories))
                        {
                            try
                            {
                                FileInfo fi = new FileInfo(file);
                                files.Add(new KeyValuePair<string, long>(fi.FullName, fi.Length));
                                totalSize += fi.Length;
                                totalCount++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
                files.Sort((a, b) => b.Value.CompareTo(a.Value));
                int count = 0;
                foreach (var kv in files)
                {
                    if (count >= 20) break;
                    string sizeStr = "";
                    if (kv.Value >= 1073741824) sizeStr = (kv.Value / 1073741824.0).ToString("F2") + " GB";
                    else if (kv.Value >= 1048576) sizeStr = (kv.Value / 1048576.0).ToString("F2") + " MB";
                    else if (kv.Value >= 1024) sizeStr = (kv.Value / 1024.0).ToString("F2") + " KB";
                    else sizeStr = kv.Value + " B";
                    listView.Items.Add(new ListViewItem(new string[] { kv.Key, sizeStr }));
                    count++;
                }
                string totalStr = "";
                if (totalSize >= 1073741824) totalStr = (totalSize / 1073741824.0).ToString("F2") + " GB";
                else if (totalSize >= 1048576) totalStr = (totalSize / 1048576.0).ToString("F2") + " MB";
                else totalStr = (totalSize / 1024.0).ToString("F2") + " KB";
                lblInfo.Text = "Total: " + totalStr + " across " + totalCount + " files (showing top 20)";
            }
            catch { lblInfo.Text = "Error scanning drive"; }
            btnScan.Enabled = true;
            btnScan.Text = "Scan";
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); monoFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblDrive, cmbDrive, btnScan, listView, lblInfo });
        f.Show();
        SetStatus("Disk Analyzer opened");
    }

    static void OpenCsvViewer()
    {
        var f = new Form();
        f.Text = "GM - CSV Viewer";
        f.Size = new Size(600, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        Font monoFont = new Font("Consolas", 9);

        var btnLoad = new Button { Text = "Load CSV", Location = new Point(10, 10), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 180), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLoad.FlatAppearance.BorderSize = 0;
        var lblInfo = new Label { Text = "No file loaded", Font = lblFont, ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(120, 17) };

        var grid = new DataGridView { Font = monoFont, Size = new Size(570, 330), Location = new Point(10, 50), BackgroundColor = Color.FromArgb(20, 20, 35), ForeColor = Color.White, GridColor = Color.FromArgb(40, 40, 60), BorderStyle = BorderStyle.None, AllowUserToAddRows = false, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };

        btnLoad.Click += (s, e) =>
        {
            string path = PromptFilePath("GM - Select CSV File", "CSV files (*.csv)");
            if (path == null) return;
            try
            {
                grid.Columns.Clear();
                grid.Rows.Clear();
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0) { lblInfo.Text = "File is empty"; return; }

                string[] headers = ParseCsvLine(lines[0]);
                foreach (string h in headers)
                    grid.Columns.Add(h.Trim(), h.Trim());

                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Trim().Length == 0) continue;
                    string[] cells = ParseCsvLine(lines[i]);
                    grid.Rows.Add(cells);
                }
                lblInfo.Text = Path.GetFileName(path) + " - " + grid.Rows.Count + " rows, " + grid.Columns.Count + " columns";
            }
            catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
        };

        f.FormClosed += (s, e) => { lblFont.Dispose(); btnFont.Dispose(); monoFont.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { btnLoad, lblInfo, grid });
        f.Show();
        SetStatus("CSV Viewer opened");
    }

    static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        string current = "";
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current); current = ""; }
            else { current += c; }
        }
        result.Add(current);
        return result.ToArray();
    }

    static void OpenJsonToCsv()
    {
        var f = new Form();
        f.Text = "GM - JSON to CSV Converter";
        f.Size = new Size(550, 480);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "JSON Input ([{key:value}, ...]):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(510, 130), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblOutput = new Label { Text = "CSV Output:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 170) };
        var txtOutput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Size = new Size(510, 180), Location = new Point(10, 190), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 380) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnConvert = new Button { Text = "Convert", Location = new Point(10, 405), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnConvert.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy CSV", Location = new Point(120, 405), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(230, 405), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;

        btnConvert.Click += (s, e) =>
        {
            try
            {
                string input = txtInput.Text.Trim();
                if (input.Length == 0) { lblStatus.Text = "Enter JSON array"; return; }
                var rows = new List<Dictionary<string, string>>();
                var keys = new List<string>();
                string buf = input.Trim();
                if (buf.StartsWith("[")) buf = buf.Substring(1);
                if (buf.EndsWith("]")) buf = buf.Substring(0, buf.Length - 1);
                int idx = 0;
                while (idx < buf.Length)
                {
                    while (idx < buf.Length && buf[idx] != '{') idx++;
                    if (idx >= buf.Length) break;
                    idx++;
                    var obj = new Dictionary<string, string>();
                    while (idx < buf.Length && buf[idx] != '}')
                    {
                        while (idx < buf.Length && (buf[idx] == ' ' || buf[idx] == ',' || buf[idx] == '\n' || buf[idx] == '\r' || buf[idx] == '\t')) idx++;
                        if (idx >= buf.Length || buf[idx] == '}') break;
                        if (buf[idx] == '"')
                        {
                            idx++;
                            string key = "";
                            while (idx < buf.Length && buf[idx] != '"') { key += buf[idx]; idx++; }
                            idx++;
                            while (idx < buf.Length && buf[idx] != ':') idx++;
                            idx++;
                            while (idx < buf.Length && (buf[idx] == ' ' || buf[idx] == '\t')) idx++;
                            string val = "";
                            if (idx < buf.Length && buf[idx] == '"')
                            {
                                idx++;
                                while (idx < buf.Length && buf[idx] != '"') { val += buf[idx]; idx++; }
                                idx++;
                            }
                            else
                            {
                                while (idx < buf.Length && buf[idx] != ',' && buf[idx] != '}' && buf[idx] != '\n') { val += buf[idx]; idx++; }
                                val = val.Trim();
                            }
                            obj[key] = val;
                            if (!keys.Contains(key)) keys.Add(key);
                        }
                        else { idx++; }
                    }
                    if (obj.Count > 0) rows.Add(obj);
                    idx++;
                }
                if (rows.Count == 0) { lblStatus.Text = "No objects found"; return; }
                StringBuilder sb = new StringBuilder();
                for (int k = 0; k < keys.Count; k++) { if (k > 0) sb.Append(","); sb.Append(keys[k]); }
                sb.AppendLine();
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int k = 0; k < keys.Count; k++)
                    {
                        if (k > 0) sb.Append(",");
                        string v = "";
                        rows[r].TryGetValue(keys[k], out v);
                        if (v.Contains(",") || v.Contains("\"") || v.Contains("\n"))
                            v = "\"" + v.Replace("\"", "\"\"") + "\"";
                        sb.Append(v);
                    }
                    sb.AppendLine();
                }
                txtOutput.Text = sb.ToString();
                lblStatus.Text = "Converted " + rows.Count + " rows, " + keys.Count + " columns";
            }
            catch { lblStatus.Text = "Parse error"; }
        };
        btnCopy.Click += (s, e) => { try { if (txtOutput.Text.Length > 0) { Clipboard.SetText(txtOutput.Text); lblStatus.Text = "CSV copied"; } } catch { } };
        btnClear.Click += (s, e) => { txtInput.Text = ""; txtOutput.Text = ""; lblStatus.Text = ""; };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblInput.Font.Dispose(); txtInput.Font.Dispose(); lblOutput.Font.Dispose(); txtOutput.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtInput, lblOutput, txtOutput, btnConvert, btnCopy, btnClear, lblStatus });
        f.Show();
        SetStatus("JSON to CSV Converter opened");
    }

    static void OpenWordCounter()
    {
        var f = new Form();
        f.Text = "GM - Word Counter";
        f.Size = new Size(500, 380);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblText = new Label { Text = "Input text:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtText = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(460, 120), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblWords = new Label { Text = "Words: 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 165) };
        var lblChars = new Label { Text = "Characters: 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 190) };
        var lblCharsNoSpace = new Label { Text = "Chars (no spaces): 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 215) };
        var lblLines = new Label { Text = "Lines: 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 240) };
        var lblSentences = new Label { Text = "Sentences: 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 265) };
        var lblParagraphs = new Label { Text = "Paragraphs: 0", Font = new Font("Segoe UI", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 290) };
        var lblReading = new Label { Text = "Reading time: ~0 min", Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(250, 165) };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 325) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnCopy = new Button { Text = "Copy Stats", Location = new Point(380, 290), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        Action updateStats = () =>
        {
            string text = txtText.Text;
            int charCount = text.Length;
            int charNoSpace = text.Replace(" ", "").Replace("\n", "").Replace("\r", "").Length;
            int wordCount = text.Trim().Length == 0 ? 0 : text.Trim().Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int lineCount = text.Length == 0 ? 0 : text.Split('\n').Length;
            int sentenceCount = 0;
            foreach (char c in text) { if (c == '.' || c == '!' || c == '?') sentenceCount++; }
            int paraCount = text.Trim().Length == 0 ? 0 : text.Split(new string[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
            int readingMin = (int)Math.Ceiling(wordCount / 200.0);
            lblWords.Text = "Words: " + wordCount;
            lblChars.Text = "Characters: " + charCount;
            lblCharsNoSpace.Text = "Chars (no spaces): " + charNoSpace;
            lblLines.Text = "Lines: " + lineCount;
            lblSentences.Text = "Sentences: " + sentenceCount;
            lblParagraphs.Text = "Paragraphs: " + paraCount;
            lblReading.Text = "Reading time: ~" + readingMin + " min";
        };
        txtText.TextChanged += (s, e) => updateStats();
        btnCopy.Click += (s, e) =>
        {
            try
            {
                string stats = "Words: " + lblWords.Text + "\n" + lblChars.Text + "\n" + lblCharsNoSpace.Text + "\n" + lblLines.Text + "\n" + lblSentences.Text + "\n" + lblParagraphs.Text + "\n" + lblReading.Text;
                Clipboard.SetText(stats);
                lblStatus.Text = "Stats copied";
            }
            catch { }
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblText.Font.Dispose(); txtText.Font.Dispose(); lblWords.Font.Dispose(); lblChars.Font.Dispose(); lblCharsNoSpace.Font.Dispose(); lblLines.Font.Dispose(); lblSentences.Font.Dispose(); lblParagraphs.Font.Dispose(); lblReading.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblText, txtText, lblWords, lblChars, lblCharsNoSpace, lblLines, lblSentences, lblParagraphs, lblReading, btnCopy, lblStatus });
        f.Show();
        SetStatus("Word Counter opened");
    }

    static void OpenTextToSpeech()
    {
        var f = new Form();
        f.Text = "GM - Text to Speech";
        f.Size = new Size(580, 600);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        Font lblFont = new Font("Segoe UI", 9);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        Font bigFont = new Font("Segoe UI", 11, FontStyle.Bold);
        Font monoFont = new Font("Consolas", 9);

        var lblText = new Label { Text = "Text to speak:", Font = bigFont, ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(10, 10) };
        var txtText = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(540, 90), Location = new Point(10, 35), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

        var lblVoice = new Label { Text = "Voice:", Font = bigFont, ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(10, 135) };
        var cmbVoice = new ComboBox { Font = new Font("Segoe UI", 9), Size = new Size(320, 25), Location = new Point(10, 160), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, DropDownStyle = ComboBoxStyle.DropDownList };

        var lblEffect = new Label { Text = "Effect:", Font = bigFont, ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(350, 135) };
        var cmbEffect = new ComboBox { Font = new Font("Segoe UI", 9), Size = new Size(200, 25), Location = new Point(350, 160), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEffect.Items.AddRange(new object[] {
            "Normal",
            "Deep & Slow",
            "High & Fast",
            "Whisper",
            "Robot",
            "Echo Pauses",
            "Slow Motion",
            "Chipmunk",
            "Demonic",
            "Old Man",
            "Narrator",
            "Evil Villain",
            "Alien",
            "Ghost",
            "Chipmunk 2X",
            "Darth Vader"
        });
        cmbEffect.SelectedIndex = 0;

        var lblRate = new Label { Text = "Speed:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 200) };
        var trkRate = new TrackBar { Minimum = -10, Maximum = 10, Value = 0, Location = new Point(70, 195), Size = new Size(160, 30), TickFrequency = 2 };
        var lblRateVal = new Label { Text = "0", Font = lblFont, ForeColor = Color.FromArgb(0, 200, 100), AutoSize = true, Location = new Point(235, 200) };
        trkRate.ValueChanged += (s, e) => { lblRateVal.Text = trkRate.Value.ToString(); };

        var lblVol = new Label { Text = "Volume:", Font = lblFont, ForeColor = Color.White, AutoSize = true, Location = new Point(270, 200) };
        var trkVol = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, Location = new Point(340, 195), Size = new Size(160, 30), TickFrequency = 10 };
        var lblVolVal = new Label { Text = "100%", Font = lblFont, ForeColor = Color.FromArgb(0, 200, 100), AutoSize = true, Location = new Point(505, 200) };
        trkVol.ValueChanged += (s, e) => { lblVolVal.Text = trkVol.Value.ToString() + "%"; };

        var btnSpeak = new Button { Text = "Speak", Location = new Point(10, 240), Size = new Size(80, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 150, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSpeak.FlatAppearance.BorderSize = 0;
        var btnStop = new Button { Text = "Stop", Location = new Point(100, 240), Size = new Size(80, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStop.FlatAppearance.BorderSize = 0;
        var btnPause = new Button { Text = "Pause", Location = new Point(190, 240), Size = new Size(80, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 100, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnPause.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy Script", Location = new Point(280, 240), Size = new Size(100, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;
        var btnInstall = new Button { Text = "Install More Voices", Location = new Point(390, 240), Size = new Size(140, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 80, 140), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnInstall.FlatAppearance.BorderSize = 0;

        var lblAvail = new Label { Text = "Installed Voices:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(10, 290) };
        var txtVoices = new TextBox { Multiline = true, ReadOnly = true, Font = monoFont, Size = new Size(540, 80), Location = new Point(10, 310), BackColor = Color.FromArgb(25, 25, 35), ForeColor = Color.FromArgb(100, 200, 100), BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical };

        var lblEffectInfo = new Label { Text = "Effect Info:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(10, 400) };
        var txtEffectInfo = new TextBox { Multiline = true, ReadOnly = true, Font = monoFont, Size = new Size(540, 55), Location = new Point(10, 420), BackColor = Color.FromArgb(25, 25, 35), ForeColor = Color.FromArgb(200, 200, 100), BorderStyle = BorderStyle.FixedSingle };

        string[] effectDesc = new string[] {
            "Normal: Standard voice. No modifications.",
            "Deep & Slow: Rate -5. Deep, menacing voice. Great for horror.",
            "High & Fast: Rate +6. High-pitched, frantic. Good for comedy.",
            "Whisper: Volume 40%, rate -2. Soft and eerie. Ghostly.",
            "Robot: Rate 0. Add '...' between words for robotic pauses.",
            "Echo Pauses: Rate 0. Type words with '...' for echo effect.",
            "Slow Motion: Rate -8. Extremely slow. Dream-like.",
            "Chipmunk: Rate +8. Very high-pitched. Funny readings.",
            "Demonic: Rate -9, volume 80%. Terrifying depth.",
            "Old Man: Rate -1, volume 85%. Raspy elder voice.",
            "Narrator: Rate 0, volume 100%. Professional documentary.",
            "Evil Villain: Rate -6, volume 110%. Mwahahaha!",
            "Alien: Rate +2, volume 70%. Unnatural, otherworldly.",
            "Ghost: Rate -3, volume 30%. Barely audible whisper.",
            "Chipmunk 2X: Rate +10. Maximum chipmunk. Unhinged.",
            "Darth Vader: Rate -4, volume 90%. Heavy breathing pace."
        };

        cmbEffect.SelectedIndexChanged += (s, e) =>
        {
            if (cmbEffect.SelectedIndex >= 0 && cmbEffect.SelectedIndex < effectDesc.Length)
                txtEffectInfo.Text = effectDesc[cmbEffect.SelectedIndex];
            int[] rates = new int[] { 0, -5, 6, -2, 0, 0, -8, 8, -9, -1, 0, -6, 2, -3, 10, -4 };
            int[] vols = new int[] { 100, 100, 100, 40, 100, 100, 100, 100, 80, 85, 100, 110, 70, 30, 100, 90 };
            if (cmbEffect.SelectedIndex >= 0 && cmbEffect.SelectedIndex < rates.Length)
            {
                trkRate.Value = rates[cmbEffect.SelectedIndex];
                trkVol.Value = vols[cmbEffect.SelectedIndex];
            }
        };

        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8, FontStyle.Italic), ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(10, 490) };

        Action loadVoices = () =>
        {
            cmbVoice.Items.Clear();
            txtVoices.Text = "";
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-Command \"Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; foreach ($v in $s.GetInstalledVoices()) { Write-Host $v.VoiceInfo.Name '|' $v.VoiceInfo.Culture.Name '|' $v.VoiceInfo.Gender }\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                p.Dispose();
                string[] lines = output.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith("At ") && !t.StartsWith("+") && !t.StartsWith("WARNING") && !t.StartsWith("Title"))
                    {
                        cmbVoice.Items.Add(t);
                        txtVoices.Text += t + Environment.NewLine;
                    }
                }
                if (cmbVoice.Items.Count == 0)
                {
                    cmbVoice.Items.Add("Microsoft David Desktop | en-US | Male");
                    cmbVoice.Items.Add("Microsoft Zira Desktop | en-US | Female");
                    txtVoices.Text = "Fallback: David (Male), Zira (Female)" + Environment.NewLine + "Click 'Install More Voices' to add voices with different accents.";
                }
                cmbVoice.SelectedIndex = 0;
            }
            catch
            {
                cmbVoice.Items.Add("Microsoft David Desktop | en-US | Male");
                cmbVoice.Items.Add("Microsoft Zira Desktop | en-US | Female");
                txtVoices.Text = "Could not detect voices.";
                cmbVoice.SelectedIndex = 0;
            }
        };
        loadVoices();

        btnInstall.Click += (s, e) =>
        {
            var result = MessageBox.Show(
                "Windows has additional voices you can install for FREE." + Environment.NewLine + Environment.NewLine +
                "Different languages = genuinely different sounding voices:" + Environment.NewLine +
                "  - Microsoft Heera (Indian English Female) - warm, melodic" + Environment.NewLine +
                "  - Microsoft Ravi (Indian English Male) - deep, resonant" + Environment.NewLine +
                "  - Microsoft Irina (Russian Female) - exotic, mysterious" + Environment.NewLine +
                "  - Microsoft Pablo (Spanish Male) - smooth, passionate" + Environment.NewLine +
                "  - Microsoft Huihui (Chinese Female) - elegant, precise" + Environment.NewLine +
                "  - Microsoft Haruka (Japanese Female) - soft, refined" + Environment.NewLine +
                "  - Microsoft Kangkang (Chinese Male) - authoritative" + Environment.NewLine +
                "  - Microsoft Sidartha (Hindi Male) - warm, expressive" + Environment.NewLine + Environment.NewLine +
                "These are NOT just speed changes - they are completely different voices" + Environment.NewLine +
                "with different accents, tones, and characteristics." + Environment.NewLine + Environment.NewLine +
                "Open Windows Speech Settings to install?",
                "Install Different Voices",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                try { Process.Start("ms-settings:speech"); }
                catch { try { Process.Start("control", "intl.cpl"); } catch { } }
            }
        };

        Process ttsProc = null;
        Action<string> speak = (text) =>
        {
            if (text.Length == 0) { lblStatus.Text = "Enter text first"; return; }
            try
            {
                if (ttsProc != null) { try { ttsProc.Kill(); } catch { } ttsProc.Dispose(); ttsProc = null; }
                int rate = trkRate.Value;
                int vol = trkVol.Value;
                string voiceLine = cmbVoice.SelectedItem.ToString();
                string voiceName = voiceLine.Split(new char[] { '|' })[0].Trim();
                string safeText = text.Replace("'", "''").Replace("$", "`$").Replace("(", "`(").Replace(")", "`)");
                string safeVoice = voiceName.Replace("'", "''");
                string script = "$s = New-Object -ComObject SAPI.SPVoice; $s.Rate = " + rate + "; $s.Volume = " + vol + "; $s.SelectVoice('" + safeVoice + "'); $s.Speak('" + safeText + "')";
                var psi = new ProcessStartInfo("powershell.exe", "-Command \"" + script + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                ttsProc = Process.Start(psi);
                lblStatus.Text = "Speaking... [" + voiceName + "] [" + cmbEffect.SelectedItem.ToString() + "]";
            }
            catch { lblStatus.Text = "Speech error"; }
        };

        btnSpeak.Click += (s, e) => speak(txtText.Text);
        btnStop.Click += (s, e) =>
        {
            try
            {
                if (ttsProc != null && !ttsProc.HasExited)
                {
                    ttsProc.Kill(); ttsProc.Dispose(); ttsProc = null;
                    lblStatus.Text = "Stopped";
                }
            }
            catch { lblStatus.Text = "Stop error"; }
        };
        btnPause.Click += (s, e) =>
        {
            try
            {
                if (ttsProc != null && !ttsProc.HasExited)
                {
                    ttsProc.Kill(); ttsProc.Dispose(); ttsProc = null;
                    lblStatus.Text = "Paused (restart to resume)";
                }
            }
            catch { }
        };
        btnCopy.Click += (s, e) =>
        {
            try
            {
                int rate = trkRate.Value;
                int vol = trkVol.Value;
                string voiceLine = cmbVoice.SelectedItem.ToString();
                string voiceName = voiceLine.Split(new char[] { '|' })[0].Trim();
                string safeText = txtText.Text.Replace("'", "''").Replace("$", "`$");
                string script = "# GM Text to Speech" + Environment.NewLine;
                script += "$s = New-Object -ComObject SAPI.SPVoice" + Environment.NewLine;
                script += "$s.Rate = " + rate + Environment.NewLine;
                script += "$s.Volume = " + vol + Environment.NewLine;
                script += "$s.SelectVoice('" + voiceName + "')" + Environment.NewLine;
                script += "$s.Speak('" + safeText + "')";
                Clipboard.SetText(script);
                lblStatus.Text = "Script copied to clipboard";
            }
            catch { }
        };

        cmbEffect.SelectedIndex = 0;

        f.FormClosed += (s, e) =>
        {
            if (ttsProc != null) { try { if (!ttsProc.HasExited) ttsProc.Kill(); } catch { } ttsProc.Dispose(); }
            btnFont.Dispose(); lblFont.Dispose(); bigFont.Dispose(); monoFont.Dispose();
            lblText.Font.Dispose(); txtText.Font.Dispose(); lblRate.Font.Dispose();
            trkRate.Font.Dispose(); lblVol.Font.Dispose(); trkVol.Font.Dispose();
            lblVolVal.Font.Dispose(); lblRateVal.Font.Dispose();
            cmbVoice.Font.Dispose(); cmbEffect.Font.Dispose();
            lblStatus.Font.Dispose(); ico.Dispose();
            lblAvail.Font.Dispose(); txtVoices.Font.Dispose();
            lblEffectInfo.Font.Dispose(); txtEffectInfo.Font.Dispose();
            btnInstall.Font.Dispose();
        };
        f.Controls.AddRange(new Control[] { lblText, txtText, lblVoice, cmbVoice, lblEffect, cmbEffect, lblRate, trkRate, lblRateVal, lblVol, trkVol, lblVolVal, btnSpeak, btnStop, btnPause, btnCopy, btnInstall, lblAvail, txtVoices, lblEffectInfo, txtEffectInfo, lblStatus });
        f.Show();
        SetStatus("Text to Speech opened");
    }

    static void OpenPasswordStrength()
    {
        var f = new Form();
        f.Text = "GM - Password Strength Checker";
        f.Size = new Size(420, 340);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblInput = new Label { Text = "Enter password:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtPassword = new TextBox { Font = new Font("Consolas", 12), Size = new Size(380, 28), Location = new Point(10, 30), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
        var barStrength = new ProgressBar { Location = new Point(10, 70), Size = new Size(380, 25), Style = ProgressBarStyle.Continuous };
        var lblScore = new Label { Text = "Score: 0/100", Font = new Font("Consolas", 12, FontStyle.Bold), ForeColor = Color.Red, AutoSize = true, Location = new Point(10, 105) };
        var lblLen = new Label { Text = "Length: 0", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 140) };
        var lblUpper = new Label { Text = "Uppercase: No", Font = new Font("Segoe UI", 9), ForeColor = Color.Red, AutoSize = true, Location = new Point(10, 165) };
        var lblLower = new Label { Text = "Lowercase: No", Font = new Font("Segoe UI", 9), ForeColor = Color.Red, AutoSize = true, Location = new Point(10, 190) };
        var lblDigit = new Label { Text = "Digits: No", Font = new Font("Segoe UI", 9), ForeColor = Color.Red, AutoSize = true, Location = new Point(10, 215) };
        var lblSpecial = new Label { Text = "Special Chars: No", Font = new Font("Segoe UI", 9), ForeColor = Color.Red, AutoSize = true, Location = new Point(10, 240) };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 280) };

        Action analyze = null;
        analyze = () =>
        {
            string pw = txtPassword.Text;
            int score = 0;
            bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
            foreach (char c in pw)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else hasSpecial = true;
            }
            score += Math.Min(pw.Length * 4, 40);
            if (hasUpper) score += 15;
            if (hasLower) score += 15;
            if (hasDigit) score += 15;
            if (hasSpecial) score += 15;
            if (pw.Length >= 12) score += 5;
            score = Math.Min(score, 100);
            barStrength.Value = score;
            lblScore.Text = "Score: " + score + "/100";
            lblLen.Text = "Length: " + pw.Length;
            lblUpper.Text = "Uppercase: " + (hasUpper ? "Yes" : "No");
            lblUpper.ForeColor = hasUpper ? Color.Lime : Color.Red;
            lblLower.Text = "Lowercase: " + (hasLower ? "Yes" : "No");
            lblLower.ForeColor = hasLower ? Color.Lime : Color.Red;
            lblDigit.Text = "Digits: " + (hasDigit ? "Yes" : "No");
            lblDigit.ForeColor = hasDigit ? Color.Lime : Color.Red;
            lblSpecial.Text = "Special Chars: " + (hasSpecial ? "Yes" : "No");
            lblSpecial.ForeColor = hasSpecial ? Color.Lime : Color.Red;
            if (score < 40) { lblScore.ForeColor = Color.Red; lblStatus.Text = "Weak password"; }
            else if (score <= 70) { lblScore.ForeColor = Color.Yellow; lblStatus.Text = "Moderate password"; }
            else { lblScore.ForeColor = Color.Lime; lblStatus.Text = "Strong password"; }
        };

        txtPassword.TextChanged += (s, e) => analyze();
        analyze();

        f.FormClosed += (s, e) => { lblInput.Font.Dispose(); txtPassword.Font.Dispose(); lblScore.Font.Dispose(); lblLen.Font.Dispose(); lblUpper.Font.Dispose(); lblLower.Font.Dispose(); lblDigit.Font.Dispose(); lblSpecial.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblInput, txtPassword, barStrength, lblScore, lblLen, lblUpper, lblLower, lblDigit, lblSpecial, lblStatus });
        f.Show();
        SetStatus("Password Strength Checker opened");
    }

    static void OpenPortScanner()
    {
        var f = new Form();
        f.Text = "GM - Port Scanner";
        f.Size = new Size(450, 400);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblHost = new Label { Text = "Host:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtHost = new TextBox { Font = new Font("Consolas", 10), Size = new Size(200, 25), Location = new Point(55, 9), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "127.0.0.1" };
        var lblRange = new Label { Text = "Port range:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(270, 12) };
        var txtRange = new TextBox { Font = new Font("Consolas", 10), Size = new Size(100, 25), Location = new Point(360, 9), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "1-1024" };
        var listPorts = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(15, 15, 25), ForeColor = Color.FromArgb(0, 200, 100), Size = new Size(420, 220), Location = new Point(10, 45) };
        var lblStatus = new Label { Text = "Enter host and port range, then click Scan", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 275) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        bool scanning = false;
        var btnScan = new Button { Text = "Scan", Location = new Point(10, 300), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnScan.FlatAppearance.BorderSize = 0;
        var btnStop = new Button { Text = "Stop", Location = new Point(100, 300), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 40, 40), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnStop.FlatAppearance.BorderSize = 0;
        btnStop.Enabled = false;
        var btnClear = new Button { Text = "Clear", Location = new Point(190, 300), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;
        var btnCopy = new Button { Text = "Copy", Location = new Point(280, 300), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnCopy.FlatAppearance.BorderSize = 0;

        btnScan.Click += (s, e) =>
        {
            string host = txtHost.Text.Trim();
            if (host.Length == 0) { lblStatus.Text = "Enter a host"; return; }
            string range = txtRange.Text.Trim();
            int dash = range.IndexOf('-');
            if (dash < 0) { lblStatus.Text = "Use format: start-end (e.g. 1-1024)"; return; }
            int startPort, endPort;
            if (!int.TryParse(range.Substring(0, dash), out startPort) || !int.TryParse(range.Substring(dash + 1), out endPort)) { lblStatus.Text = "Invalid port range"; return; }
            if (startPort < 1 || endPort > 65535 || startPort > endPort) { lblStatus.Text = "Port range must be 1-65535"; return; }
            listPorts.Items.Clear();
            scanning = true;
            btnScan.Enabled = false;
            btnStop.Enabled = true;
            txtHost.Enabled = false;
            txtRange.Enabled = false;
            int found = 0;
            int scanned = 0;
            int total = endPort - startPort + 1;
            Task.Factory.StartNew(() =>
            {
                for (int port = startPort; port <= endPort; port++)
                {
                    if (!scanning) break;
                    try
                    {
                        using (var tcp = new System.Net.Sockets.TcpClient())
                        {
                            var ar = tcp.BeginConnect(host, port, null, null);
                            bool connected = ar.AsyncWaitHandle.WaitOne(300, false);
                            if (connected && tcp.Connected)
                            {
                                try { f.Invoke((Action)(() => { listPorts.Items.Add("Port " + port + " OPEN"); })); found++; }
                                catch { }
                            }
                            try { tcp.EndConnect(ar); } catch { }
                        }
                    }
                    catch { }
                    scanned++;
                    if (scanned % 50 == 0)
                    {
                        try { f.Invoke((Action)(() => { lblStatus.Text = "Scanned " + scanned + "/" + total + " ports..."; })); } catch { }
                    }
                }
                try { f.Invoke((Action)(() =>
                {
                    scanning = false;
                    btnScan.Enabled = true;
                    btnStop.Enabled = false;
                    txtHost.Enabled = true;
                    txtRange.Enabled = true;
                    lblStatus.Text = "Done. Found " + found + " open port(s) from " + total + " scanned.";
                })); } catch { }
            });
        };
        btnStop.Click += (s, e) => { scanning = false; lblStatus.Text = "Stopping..."; };
        btnClear.Click += (s, e) => { listPorts.Items.Clear(); lblStatus.Text = "Cleared"; };
        btnCopy.Click += (s, e) =>
        {
            if (listPorts.Items.Count == 0) { lblStatus.Text = "Nothing to copy"; return; }
            string result = "";
            foreach (var item in listPorts.Items) result += item.ToString() + "\n";
            try { Clipboard.SetText(result); lblStatus.Text = "Copied " + listPorts.Items.Count + " ports"; } catch { }
        };

        f.FormClosed += (s, e) => { scanning = false; btnFont.Dispose(); lblHost.Font.Dispose(); txtHost.Font.Dispose(); lblRange.Font.Dispose(); txtRange.Font.Dispose(); listPorts.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblHost, txtHost, lblRange, txtRange, listPorts, btnScan, btnStop, btnClear, btnCopy, lblStatus });
        f.Show();
        SetStatus("Port Scanner opened");
    }

    static void OpenIpGeo()
    {
        var f = new Form();
        f.Text = "GM - IP Geolocation";
        f.Size = new Size(420, 360);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblIp = new Label { Text = "IP Address (blank=detect):", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtIp = new TextBox { Font = new Font("Consolas", 10), Size = new Size(250, 25), Location = new Point(10, 32), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var txtResult = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(380, 200), Location = new Point(10, 100), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 310) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnLookup = new Button { Text = "Lookup", Location = new Point(10, 60), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnLookup.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(120, 60), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;

        btnLookup.Click += (s, e) =>
        {
            string ip = txtIp.Text.Trim();
            string url = "https://ip-api.com/json/";
            if (ip.Length > 0) url += ip;
            lblStatus.Text = "Looking up...";
            txtResult.Text = "";
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Timeout = 8000;
                    req.UserAgent = "GM";
                    using (var resp = req.GetResponse())
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    {
                        string json = sr.ReadToEnd();
                        string country = "", city = "", isp = "", lat = "", lon = "", tz = "", query = "";
                        int ci = json.IndexOf("\"country\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 11); int e2 = json.IndexOf("\"", s2 + 1); country = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"city\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 8); int e2 = json.IndexOf("\"", s2 + 1); city = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"isp\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 6); int e2 = json.IndexOf("\"", s2 + 1); isp = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"lat\":");
                        if (ci >= 0) { int s2 = ci + 6; int e2 = json.IndexOf(",", s2); if (e2 < 0) e2 = json.IndexOf("}", s2); lat = json.Substring(s2, e2 - s2).Trim(); }
                        ci = json.IndexOf("\"lon\":");
                        if (ci >= 0) { int s2 = ci + 6; int e2 = json.IndexOf(",", s2); if (e2 < 0) e2 = json.IndexOf("}", s2); lon = json.Substring(s2, e2 - s2).Trim(); }
                        ci = json.IndexOf("\"timezone\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 12); int e2 = json.IndexOf("\"", s2 + 1); tz = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"query\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 8); int e2 = json.IndexOf("\"", s2 + 1); query = json.Substring(s2 + 1, e2 - s2 - 1); }
                        string result = "IP: " + query + "\n";
                        result += "Country: " + country + "\n";
                        result += "City: " + city + "\n";
                        result += "ISP: " + isp + "\n";
                        result += "Latitude: " + lat + "\n";
                        result += "Longitude: " + lon + "\n";
                        result += "Timezone: " + tz + "\n";
                        try { f.Invoke((Action)(() => { txtResult.Text = result; lblStatus.Text = "Lookup complete for " + query; })); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { f.Invoke((Action)(() => { lblStatus.Text = "Error: " + ex.Message; })); } catch { }
                }
            });
        };
        btnClear.Click += (s, e) => { txtIp.Text = ""; txtResult.Text = ""; lblStatus.Text = ""; };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblIp.Font.Dispose(); txtIp.Font.Dispose(); txtResult.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblIp, txtIp, btnLookup, btnClear, txtResult, lblStatus });
        f.Show();
        SetStatus("IP Geolocation opened");
    }

    static void OpenWeather()
    {
        var f = new Form();
        f.Text = "GM - Weather Lookup";
        f.Size = new Size(420, 370);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblCity = new Label { Text = "City name:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 12) };
        var txtCity = new TextBox { Font = new Font("Consolas", 10), Size = new Size(250, 25), Location = new Point(90, 9), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var txtResult = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Size = new Size(380, 210), Location = new Point(10, 100), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 320) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        var btnGet = new Button { Text = "Get Weather", Location = new Point(10, 50), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnGet.FlatAppearance.BorderSize = 0;
        var btnClear = new Button { Text = "Clear", Location = new Point(130, 50), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnClear.FlatAppearance.BorderSize = 0;

        btnGet.Click += (s, e) =>
        {
            string city = txtCity.Text.Trim();
            if (city.Length == 0) { lblStatus.Text = "Enter a city name"; return; }
            lblStatus.Text = "Fetching weather...";
            txtResult.Text = "";
            Task.Factory.StartNew(() =>
            {
                try
                {
                    string url = "https://wttr.in/" + Uri.EscapeDataString(city) + "?format=j1";
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Timeout = 10000;
                    req.UserAgent = "GM";
                    using (var resp = req.GetResponse())
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    {
                        string json = sr.ReadToEnd();
                        string temp = "", desc = "", humidity = "", wind = "", feels = "", loc = "";
                        int ci = json.IndexOf("\"temp_C\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 10); int e2 = json.IndexOf("\"", s2 + 1); temp = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"weatherDesc\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"value\":", ci); if (s2 >= 0) { int s3 = json.IndexOf("\"", s2 + 8); int e2 = json.IndexOf("\"", s3 + 1); desc = json.Substring(s3 + 1, e2 - s3 - 1); } }
                        ci = json.IndexOf("\"humidity\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 11); int e2 = json.IndexOf("\"", s2 + 1); humidity = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"windspeedKmph\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 17); int e2 = json.IndexOf("\"", s2 + 1); wind = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"FeelsLikeC\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"", ci + 14); int e2 = json.IndexOf("\"", s2 + 1); feels = json.Substring(s2 + 1, e2 - s2 - 1); }
                        ci = json.IndexOf("\"nearest_area\":");
                        if (ci >= 0) { int s2 = json.IndexOf("\"areaName\":", ci); if (s2 >= 0) { int s3 = json.IndexOf("\"value\":", s2); if (s3 >= 0) { int s4 = json.IndexOf("\"", s3 + 8); int e2 = json.IndexOf("\"", s4 + 1); loc = json.Substring(s4 + 1, e2 - s4 - 1); } } }
                        string result = "";
                        if (loc.Length > 0) result += "Location: " + loc + "\n";
                        result += "Temperature: " + temp + " C\n";
                        result += "Feels Like: " + feels + " C\n";
                        result += "Description: " + desc + "\n";
                        result += "Humidity: " + humidity + "%\n";
                        result += "Wind Speed: " + wind + " km/h\n";
                        try { f.Invoke((Action)(() => { txtResult.Text = result; lblStatus.Text = "Weather for " + (loc.Length > 0 ? loc : city); })); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { f.Invoke((Action)(() => { lblStatus.Text = "Error: " + ex.Message; })); } catch { }
                }
            });
        };
        btnClear.Click += (s, e) => { txtCity.Text = ""; txtResult.Text = ""; lblStatus.Text = ""; };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblCity.Font.Dispose(); txtCity.Font.Dispose(); txtResult.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblCity, txtCity, btnGet, btnClear, txtResult, lblStatus });
        f.Show();
        SetStatus("Weather Lookup opened");
    }

    static void OpenCurrencyConverter()
    {
        var f = new Form();
        f.Text = "GM - Currency Converter";
        f.Size = new Size(420, 280);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblAmount = new Label { Text = "Amount:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 15) };
        var txtAmount = new TextBox { Font = new Font("Consolas", 12), Size = new Size(150, 28), Location = new Point(80, 12), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "1.00" };
        var lblFrom = new Label { Text = "From:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 55) };
        var cmbFrom = new ComboBox { Font = new Font("Segoe UI", 10), Size = new Size(100, 25), Location = new Point(60, 52), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, DropDownStyle = ComboBoxStyle.DropDownList };
        var lblTo = new Label { Text = "To:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(180, 55) };
        var cmbTo = new ComboBox { Font = new Font("Segoe UI", 10), Size = new Size(100, 25), Location = new Point(210, 52), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, DropDownStyle = ComboBoxStyle.DropDownList };
        var lblResult = new Label { Text = "Result: -", Font = new Font("Consolas", 14, FontStyle.Bold), ForeColor = Color.Lime, AutoSize = true, Location = new Point(10, 130) };
        var lblStatus = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(10, 180) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        string[] currencies = { "USD", "EUR", "GBP", "JPY", "CAD", "AUD" };
        double[] ratesToUSD = { 1.0, 0.92, 0.79, 149.5, 1.36, 1.53 };
        foreach (string c in currencies) { cmbFrom.Items.Add(c); cmbTo.Items.Add(c); }
        cmbFrom.SelectedIndex = 0;
        cmbTo.SelectedIndex = 1;

        var btnConvert = new Button { Text = "Convert", Location = new Point(10, 90), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnConvert.FlatAppearance.BorderSize = 0;
        var btnSwap = new Button { Text = "Swap", Location = new Point(120, 90), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 120), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
        btnSwap.FlatAppearance.BorderSize = 0;

        btnConvert.Click += (s, e) =>
        {
            double amount;
            if (!double.TryParse(txtAmount.Text, out amount)) { lblStatus.Text = "Enter a valid number"; return; }
            int fromIdx = cmbFrom.SelectedIndex;
            int toIdx = cmbTo.SelectedIndex;
            if (fromIdx < 0 || toIdx < 0) { lblStatus.Text = "Select currencies"; return; }
            double inUSD = amount / ratesToUSD[fromIdx];
            double result = inUSD * ratesToUSD[toIdx];
            lblResult.Text = String.Format("Result: {0:F2} {1}", result, currencies[toIdx]);
            lblStatus.Text = String.Format("{0:F2} {1} = {2:F2} {3}", amount, currencies[fromIdx], result, currencies[toIdx]);
        };
        btnSwap.Click += (s, e) =>
        {
            int tmp = cmbFrom.SelectedIndex;
            cmbFrom.SelectedIndex = cmbTo.SelectedIndex;
            cmbTo.SelectedIndex = tmp;
        };

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblAmount.Font.Dispose(); txtAmount.Font.Dispose(); lblFrom.Font.Dispose(); cmbFrom.Font.Dispose(); lblTo.Font.Dispose(); cmbTo.Font.Dispose(); lblResult.Font.Dispose(); lblStatus.Font.Dispose(); ico.Dispose(); };
        f.Controls.AddRange(new Control[] { lblAmount, txtAmount, lblFrom, cmbFrom, lblTo, cmbTo, btnConvert, btnSwap, lblResult, lblStatus });
        f.Show();
        SetStatus("Currency Converter opened");
    }

    static void OpenCharMap()
    {
        var f = new Form();
        f.Text = "GM - Character Map";
        f.Size = new Size(500, 420);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.BackColor = Color.FromArgb(15, 15, 25);
        f.StartPosition = FormStartPosition.CenterScreen;
        var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        f.Icon = ico;

        var lblSearch = new Label { Text = "Filter:", Font = new Font("Segoe UI", 9), ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10) };
        var txtSearch = new TextBox { Font = new Font("Consolas", 10), Size = new Size(200, 25), Location = new Point(55, 7), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var lblCopied = new Label { Text = "", Font = new Font("Segoe UI", 9), ForeColor = Color.Lime, AutoSize = true, Location = new Point(280, 10) };

        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

        string[] chars = {
            "\u00A9", "\u00AE", "\u2122", "\u00B0", "\u00B1", "\u00D7", "\u00F7",
            "\u2260", "\u2264", "\u2265", "\u03C0", "\u03A9", "\u00B5", "\u20AC",
            "\u00A3", "\u00A5", "\u00A7", "\u00B6", "\u00A4",
            "\u2191", "\u2193", "\u2190", "\u2192",
            "\u2605", "\u2606", "\u2713", "\u2717",
            "\u2660", "\u2663", "\u2665", "\u2666",
            "\u00B2", "\u00B3", "\u00B9", "\u00BC", "\u00BD", "\u00BE"
        };
        string[] names = {
            "Copyright", "Registered", "Trademark", "Degree", "Plus-Minus", "Multiplication", "Division",
            "Not Equal", "Less or Equal", "Greater or Equal", "Pi", "Omega", "Micro", "Euro",
            "Pound", "Yen", "Section", "Pilcrow", "Currency",
            "Up Arrow", "Down Arrow", "Left Arrow", "Right Arrow",
            "Star Filled", "Star Empty", "Check", "Cross",
            "Spade", "Club", "Heart", "Diamond",
            "Superscript 2", "Superscript 3", "Superscript 1", "Quarter", "Half", "Three-Quarter"
        };

        var panel = new FlowLayoutPanel { Location = new Point(10, 40), Size = new Size(460, 300), AutoScroll = true, BackColor = Color.FromArgb(20, 20, 35) };
        var allButtons = new List<Button>();

        for (int i = 0; i < chars.Length; i++)
        {
            string ch = chars[i];
            string nm = names[i];
            var btn = new Button
            {
                Text = ch,
                Size = new Size(50, 50),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 55),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16),
                Cursor = Cursors.Hand,
                Margin = new Padding(2),
                Tag = ch
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(60, 60, 80);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(40, 40, 55);
            btn.Click += (s, e) =>
            {
                try { Clipboard.SetText(ch); lblCopied.Text = "Copied: " + ch + " (" + nm + ")"; } catch { }
            };
            btn.Tag = nm;
            allButtons.Add(btn);
            panel.Controls.Add(btn);
        }

        Action filterButtons = () =>
        {
            string filter = txtSearch.Text.Trim().ToLower();
            panel.Controls.Clear();
            for (int i = 0; i < allButtons.Count; i++)
            {
                if (filter.Length == 0 || names[i].ToLower().Contains(filter) || chars[i].Contains(filter))
                    panel.Controls.Add(allButtons[i]);
            }
        };

        txtSearch.TextChanged += (s, e) => filterButtons();

        f.FormClosed += (s, e) => { btnFont.Dispose(); lblSearch.Font.Dispose(); txtSearch.Font.Dispose(); lblCopied.Font.Dispose(); ico.Dispose(); foreach (var b in allButtons) { try { b.Font.Dispose(); } catch { } } };
        f.Controls.AddRange(new Control[] { lblSearch, txtSearch, lblCopied, panel });
        f.Show();
        SetStatus("Character Map opened");
    }

    // ==================== HUB ====================

    class Hub : Form
    {
        Font titleFont = new Font("Segoe UI", 32, FontStyle.Bold);
        Font subFont = new Font("Segoe UI", 10);
        Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
        Font footFont = new Font("Segoe UI", 8);
        Font formFont = new Font("Segoe UI", 9);
        Font statusFont = new Font("Segoe UI", 8, FontStyle.Italic);
        NotifyIcon tray;
        ToolTip tips = new ToolTip();
        Label statusLabel;
        Button btnFocus;
        FlowLayoutPanel favPanel;
        Font lblFavFont;
        FlowLayoutPanel recentPanel;
        Font recentFont;
        Label lblRecent;
        bool ownIcon;
        bool exiting = false;
        TextBox txtSearch;
        Button btnClearSearch;
        List<Button> allToolButtons = new List<Button>();

        public Hub()
        {
            this.Text = "GM v2.9";
            this.Size = new Size(530, 900);
            this.AutoScroll = true;
            this.AutoScrollMinSize = new Size(0, 2000);
            LoadFavourites();
            LoadRecent();
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 15, 25);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = formFont;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (this.Icon != null) { ownIcon = true; }
            else { try { this.Icon = SystemIcons.Application; } catch { } }

            tray = new NotifyIcon();
            tray.Icon = this.Icon != null ? this.Icon : SystemIcons.Application;
            tray.Text = "GM Command Center";
            tray.Visible = true;

            focusTimer = new System.Windows.Forms.Timer();
            focusTimer.Interval = 60000;
            focusTimer.Tick += (s, e) =>
            {
                if (focusActive)
                {
                    TimeSpan elapsed = DateTime.Now - focusStartTime;
                    string _ft = elapsed.TotalHours >= 24 ? (int)elapsed.TotalDays + "d " + elapsed.Hours + "h " + elapsed.Minutes + "m" : (int)elapsed.TotalHours + "h " + elapsed.Minutes + "m"; SetStatus("Focus mode active: " + _ft);
                }
            };
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; this.BringToFront(); });
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Lock PC", null, (s, e) => Lock());
            trayMenu.Items.Add("Screenshot", null, (s, e) => Screenshot());
            trayMenu.Items.Add("Ping Overlay", null, (s, e) => StartPingOverlay());
            trayMenu.Items.Add("Timer", null, (s, e) => OpenTimer());
            trayMenu.Items.Add("CPU Monitor", null, (s, e) => OpenCpuMonitor());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Focus Mode", null, (s, e) => { ToggleFocus(); UpdateFocusBtn(); });
            trayMenu.Items.Add("Dark Mode", null, (s, e) => ToggleDark());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, (s, e) => { CleanupAndExit(); });
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; this.BringToFront(); };

            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                    tray.ShowBalloonTip(1000, "GM", "Running in background. Double-click tray icon to reopen.", ToolTipIcon.Info);
                    return;
                }
                CleanupAndExit();
            };

            tips.InitialDelay = 500;
            tips.ReshowDelay = 200;

            var title = new Label { Text = "GM", Font = titleFont, ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(210, 15) };
            var sub = new Label { Text = "command center", Font = subFont, ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(195, 65) };

            txtSearch = new TextBox { Font = new Font("Segoe UI", 9), Size = new Size(460, 25), Location = new Point(10, 95), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Gray, BorderStyle = BorderStyle.FixedSingle, Text = "Search tools..." };
            bool searchActive = false;
            txtSearch.Enter += (s, e) => { if (!searchActive && txtSearch.Text == "Search tools...") { txtSearch.Text = ""; txtSearch.ForeColor = Color.White; searchActive = true; } };
            txtSearch.Leave += (s, e) => { if (txtSearch.Text == "") { searchActive = false; txtSearch.Text = "Search tools..."; txtSearch.ForeColor = Color.Gray; } };
            txtSearch.TextChanged += (s, e) => { if (!searchActive) return; string q = txtSearch.Text.Trim().ToLower(); foreach (var btn in allToolButtons) { string tagStr = btn.Tag != null ? btn.Tag.ToString() : ""; btn.Visible = q.Length == 0 || btn.Text.ToLower().Contains(q) || tagStr.ToLower().Contains(q); } };
            txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { foreach (var btn in allToolButtons) { if (btn.Visible) { btn.PerformClick(); e.SuppressKeyPress = true; break; } } } };

            btnClearSearch = new Button { Text = "X", Location = new Point(475, 95), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 50, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClearSearch.FlatAppearance.BorderSize = 0;
            btnClearSearch.Click += (s, e) => { searchActive = false; txtSearch.Text = "Search tools..."; txtSearch.ForeColor = Color.Gray; foreach (var btn in allToolButtons) { btn.Visible = true; } };

            var btnRefreshAll = new Button { Text = "\u21BB", Location = new Point(505, 95), Size = new Size(25, 25), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnRefreshAll.FlatAppearance.BorderSize = 0;
            btnRefreshAll.Click += (s, e) => { searchActive = false; txtSearch.Text = "Search tools..."; txtSearch.ForeColor = Color.Gray; foreach (var btn in allToolButtons) { btn.Visible = true; } };

            recentFont = new Font("Segoe UI", 7);
            lblRecent = new Label { Text = "Recent:", Font = recentFont, ForeColor = Color.FromArgb(50, 50, 60), AutoSize = true, Location = new Point(10, 122) };
            recentPanel = new FlowLayoutPanel { AutoSize = false, Size = new Size(500, 0), Location = new Point(10, 132), FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
            RefreshRecent(recentPanel, recentFont);
            int recentHeight = 0;
            if (recentTools.Count > 0)
            {
                int buttonsPerRow = 5;
                int rows = (recentTools.Count + buttonsPerRow - 1) / buttonsPerRow;
                recentHeight = rows * 31 + 4;
            }
            recentPanel.Height = recentHeight;

            int favY = 132 + recentHeight + 4;
            favPanel = new FlowLayoutPanel { AutoSize = false, Size = new Size(500, 0), Location = new Point(10, favY + 10), FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
            RefreshFavourites(favPanel);
            lblFavFont = new Font("Segoe UI", 7);
            var lblFav = new Label { Text = "Favourites:", Font = lblFavFont, ForeColor = Color.FromArgb(50, 50, 60), AutoSize = true, Location = new Point(10, favY) };
            int favHeight = 0;
            if (favourites.Count > 0)
            {
                int buttonsPerRow = 5;
                int rows = (favourites.Count + buttonsPerRow - 1) / buttonsPerRow;
                favHeight = rows * 36 + 4;
            }
            favPanel.Height = favHeight;

            int y = favY + 18 + favHeight;
            int gap = 45;

            var btnLock = MakeBtn("Lock PC", 10, y, Color.FromArgb(0, 100, 180), (s, e) => Lock());
            tips.SetToolTip(btnLock, "Lock your PC instantly (Ctrl+L)");
            var btnClean = MakeBtn("Clean Temp", 135, y, Color.FromArgb(0, 160, 80), (s, e) => CleanTemp());
            tips.SetToolTip(btnClean, "Delete all temp files (Ctrl+D)");
            var btnDark = MakeBtn("Dark Mode", 260, y, Color.FromArgb(120, 0, 200), (s, e) => ToggleDark());
            tips.SetToolTip(btnDark, "Toggle Windows dark/light mode (Ctrl+Shift+D)");
            var btnMinAll = MakeBtn("Min All", 385, y, Color.FromArgb(40, 40, 60), (s, e) => MinimizeAll());
            tips.SetToolTip(btnMinAll, "Minimize all windows to show desktop");
            y += gap;

            var btnMute = MakeBtn("Mute Mic", 10, y, Color.FromArgb(200, 40, 40), (s, e) => ToggleMic());
            tips.SetToolTip(btnMute, "Open Sound settings to mute mic (Ctrl+M)");
            btnFocus = MakeBtn("Focus Mode", 135, y, Color.FromArgb(200, 120, 0), (s, e) => { ToggleFocus(); UpdateFocusBtn(); });
            tips.SetToolTip(btnFocus, "Block distracting websites (needs admin) (Ctrl+F / Ctrl+Shift+F=Run as Admin)");
            var btnClip = MakeBtn("Clipboard", 260, y, Color.FromArgb(0, 140, 140), (s, e) => EnableClip());
            tips.SetToolTip(btnClip, "Enable Win+V clipboard history (Ctrl+V)");
            var btnCmd = MakeBtn("Quick CMD", 385, y, Color.FromArgb(50, 50, 60), (s, e) => OpenQuickCmd());
            tips.SetToolTip(btnCmd, "Open Command Prompt (Ctrl+Shift+C)");
            y += gap;

            var btnSnap = MakeBtn("Screenshot", 10, y, Color.FromArgb(140, 0, 140), (s, e) => Screenshot());
            tips.SetToolTip(btnSnap, "Capture all screens to Desktop (Ctrl+S)");
            var btnRecord = MakeBtn("Ping Overlay", 135, y, Color.FromArgb(0, 130, 60), (s, e) => StartPingOverlay());
            tips.SetToolTip(btnRecord, "Floating ping display, draggable (Ctrl+P)");
            var btnLayout = MakeBtn("Layout", 260, y, Color.FromArgb(100, 60, 0), (s, e) => { string r = LayoutWork(); MessageBox.Show(r, "GM"); });
            tips.SetToolTip(btnLayout, "Snap Chrome + Discord side by side (Ctrl+Shift+L)");
            var btnAbout = MakeBtn("About", 385, y, Color.FromArgb(60, 40, 80), (s, e) => ShowAbout());
            tips.SetToolTip(btnAbout, "About GM Command Center");
            y += gap;

            var btnAfk = MakeBtn("AFK Launch", 10, y, Color.FromArgb(0, 80, 160), (s, e) => LaunchApps());
            tips.SetToolTip(btnAfk, "Launch Chrome, Discord, Spotify (Ctrl+R)");
            var btnVol = MakeBtn("Volume", 135, y, Color.FromArgb(80, 0, 160), (s, e) => StartVolumeControl());
            tips.SetToolTip(btnVol, "Open Windows Volume Mixer (Ctrl+U)");
            var btnNet = MakeBtn("Network", 260, y, Color.FromArgb(0, 100, 120), (s, e) => ShowNetworkInfo());
            tips.SetToolTip(btnNet, "Show network adapters, IP, gateway (Ctrl+N)");
            var btnSysInfo = MakeBtn("System Info", 385, y, Color.FromArgb(60, 60, 80), (s, e) => ShowSystemInfo());
            tips.SetToolTip(btnSysInfo, "Show OS, RAM, disk space, uptime (Ctrl+I)");
            y += gap;

            var btnRecycle = MakeBtn("Empty Bin", 10, y, Color.FromArgb(100, 50, 0), (s, e) => EmptyRecycleBin());
            tips.SetToolTip(btnRecycle, "Empty the Recycle Bin (Ctrl+B)");
            var btnProcesses = MakeBtn("Processes", 135, y, Color.FromArgb(80, 0, 60), (s, e) => ShowProcesses());
            tips.SetToolTip(btnProcesses, "Show running processes (Ctrl+T)");
            var btnTimer = MakeBtn("Timer", 260, y, Color.FromArgb(0, 120, 80), (s, e) => OpenTimer());
            tips.SetToolTip(btnTimer, "Stopwatch with lap (Ctrl+1)");
            var btnColorPick = MakeBtn("Color Picker", 385, y, Color.FromArgb(0, 80, 140), (s, e) => OpenColorPicker());
            tips.SetToolTip(btnColorPick, "Pick any pixel color (Ctrl+2)");
            y += gap;

            var btnHistClean = MakeBtn("History Clean", 10, y, Color.FromArgb(140, 80, 0), (s, e) => OpenHistoryCleaner());
            tips.SetToolTip(btnHistClean, "Clear browser history (Ctrl+3)");
            var btnShutTimer = MakeBtn("Shutdown Timer", 135, y, Color.FromArgb(160, 40, 40), (s, e) => OpenShutdownTimer());
            tips.SetToolTip(btnShutTimer, "Schedule shutdown/restart (Ctrl+4)");
            var btnMatrix = MakeBtn("Matrix Rain", 260, y, Color.FromArgb(0, 100, 0), (s, e) => OpenMatrixRain());
            tips.SetToolTip(btnMatrix, "Fullscreen matrix rain effect (Ctrl+5)");
            var btnFileHash = MakeBtn("File Hash", 385, y, Color.FromArgb(60, 60, 80), (s, e) => OpenFileHash());
            tips.SetToolTip(btnFileHash, "Calculate MD5/SHA256 hash (Ctrl+H)");
            y += gap;

            var btnCpu = MakeBtn("CPU Monitor", 10, y, Color.FromArgb(0, 120, 100), (s, e) => OpenCpuMonitor());
            tips.SetToolTip(btnCpu, "Live CPU/RAM usage monitor (Ctrl+Shift+M)");
            var btnWifi = MakeBtn("WiFi Pass", 135, y, Color.FromArgb(0, 80, 160), (s, e) => ShowWifiPasswords());
            tips.SetToolTip(btnWifi, "Show saved WiFi passwords (Ctrl+W)");
            var btnNotes = MakeBtn("Quick Notes", 260, y, Color.FromArgb(100, 60, 0), (s, e) => OpenQuickNotes());
            tips.SetToolTip(btnNotes, "Simple notepad (Ctrl+Shift+N)");
            var btnRenamer = MakeBtn("Bulk Rename", 385, y, Color.FromArgb(140, 40, 40), (s, e) => OpenBulkRenamer());
            tips.SetToolTip(btnRenamer, "Rename multiple files at once (Ctrl+Shift+B)");
            y += gap;

            var btnBase64 = MakeBtn("Base64", 10, y, Color.FromArgb(0, 80, 120), (s, e) => OpenBase64());
            tips.SetToolTip(btnBase64, "Encode/decode Base64 strings");
            var btnPassGen = MakeBtn("Password Gen", 135, y, Color.FromArgb(0, 100, 60), (s, e) => OpenPasswordGen());
            tips.SetToolTip(btnPassGen, "Generate strong random passwords");
            var btnProcPri = MakeBtn("Proc Priority", 260, y, Color.FromArgb(100, 60, 0), (s, e) => OpenProcessPriority());
            tips.SetToolTip(btnProcPri, "Change process priority (Ctrl+Shift+P)");
            var btnStartup = MakeBtn("Startup Mgr", 385, y, Color.FromArgb(60, 0, 80), (s, e) => OpenStartupManager());
            tips.SetToolTip(btnStartup, "Manage startup programs");
            y += gap;

            var btnQClean = MakeBtn("Quick Clean", 10, y, Color.FromArgb(120, 60, 0), (s, e) => OpenQuickClean());
            tips.SetToolTip(btnQClean, "Clean temp + thumbnails + recycle bin");
            var btnPubIP = MakeBtn("Public IP", 135, y, Color.FromArgb(0, 60, 100), (s, e) => ShowPublicIP());
            tips.SetToolTip(btnPubIP, "Show your public IP address");
            var btnFileInfo = MakeBtn("File Info", 260, y, Color.FromArgb(80, 80, 0), (s, e) => OpenFileInfo());
            tips.SetToolTip(btnFileInfo, "View detailed file information");
            var btnScreenTimer = MakeBtn("Screen Timer", 385, y, Color.FromArgb(100, 0, 60), (s, e) => OpenScreenshotTimer());
            tips.SetToolTip(btnScreenTimer, "Timed screenshot countdown");
            y += gap;

            var btnTextTools = MakeBtn("Text Tools", 10, y, Color.FromArgb(0, 100, 100), (s, e) => OpenTextTools());
            tips.SetToolTip(btnTextTools, "Case convert, word count, reverse");
            var btnColorPal = MakeBtn("Color Palette", 135, y, Color.FromArgb(60, 40, 100), (s, e) => OpenColorPalette());
            tips.SetToolTip(btnColorPal, "Quick color palette with hex codes");
            y += gap;

            var btnCalc = MakeBtn("Calculator", 10, y, Color.FromArgb(0, 80, 100), (s, e) => OpenCalculator());
            tips.SetToolTip(btnCalc, "Quick calculator");
            var btnUrlEnc = MakeBtn("URL Encoder", 135, y, Color.FromArgb(100, 60, 0), (s, e) => OpenUrlEncoder());
            tips.SetToolTip(btnUrlEnc, "Encode/decode URLs and HTML entities");
            var btnJson = MakeBtn("JSON Format", 260, y, Color.FromArgb(80, 0, 100), (s, e) => OpenJsonFormatter());
            tips.SetToolTip(btnJson, "Format and minify JSON");
            var btnRegex = MakeBtn("Regex Test", 385, y, Color.FromArgb(0, 60, 80), (s, e) => OpenRegexTester());
            tips.SetToolTip(btnRegex, "Test regular expressions");
            y += gap;

            var btnTextHash = MakeBtn("Text Hash", 10, y, Color.FromArgb(100, 40, 60), (s, e) => OpenTextHash());
            tips.SetToolTip(btnTextHash, "Hash text with MD5/SHA1/SHA256");
            var btnClipMgr = MakeBtn("Clipboard Mgr", 135, y, Color.FromArgb(0, 80, 120), (s, e) => OpenClipboardManager());
            tips.SetToolTip(btnClipMgr, "Store and manage multiple clipboard entries");
            var btnDriveInfo = MakeBtn("Drive Info", 260, y, Color.FromArgb(60, 80, 0), (s, e) => OpenDriveInfo());
            tips.SetToolTip(btnDriveInfo, "Detailed drive usage with visual bars");
            var btnPaint = MakeBtn("Quick Paint", 385, y, Color.FromArgb(0, 100, 80), (s, e) => OpenQuickPaint());
            tips.SetToolTip(btnPaint, "Simple drawing tool with colors");
            y += gap;

            var btnQr = MakeBtn("Pattern Code", 10, y, Color.FromArgb(80, 40, 0), (s, e) => OpenQrGenerator());
            tips.SetToolTip(btnQr, "Generate decorative pattern codes from text");
            var btnNetSpeed = MakeBtn("Net Speed", 135, y, Color.FromArgb(0, 100, 100), (s, e) => OpenNetworkSpeed());
            tips.SetToolTip(btnNetSpeed, "Test download/upload speed and latency");
            var btnHexView = MakeBtn("Hex Viewer", 260, y, Color.FromArgb(0, 80, 80), (s, e) => OpenHexViewer());
            tips.SetToolTip(btnHexView, "View files in hex editor (Ctrl+Shift+H)");
            var btnCodeFmt = MakeBtn("Code Format", 385, y, Color.FromArgb(0, 60, 100), (s, e) => OpenCodeFormatter());
            tips.SetToolTip(btnCodeFmt, "Auto-indent and format code (Ctrl+Shift+Y)");
            y += gap;

            var btnLorem = MakeBtn("Lorem Ipsum", 10, y, Color.FromArgb(80, 0, 80), (s, e) => OpenLoremIpsum());
            tips.SetToolTip(btnLorem, "Generate placeholder text (Ctrl+Shift+O)");
            var btnTimestamp = MakeBtn("Timestamp", 135, y, Color.FromArgb(100, 60, 0), (s, e) => OpenTimestampConverter());
            tips.SetToolTip(btnTimestamp, "Unix/ISO timestamp converter (Ctrl+Shift+E)");
            var btnMarkdown = MakeBtn("Markdown", 260, y, Color.FromArgb(0, 80, 40), (s, e) => OpenMarkdownPreview());
            tips.SetToolTip(btnMarkdown, "Preview markdown with live rendering (Ctrl+Shift+W)");
            var btnCssGrad = MakeBtn("CSS Gradient", 385, y, Color.FromArgb(100, 0, 60), (s, e) => OpenCssGradient());
            tips.SetToolTip(btnCssGrad, "Generate CSS gradient code (Ctrl+Shift+G)");
            y += gap;

            var btnRegexCheat = MakeBtn("Regex Cheat", 10, y, Color.FromArgb(80, 40, 0), (s, e) => OpenRegexCheat());
            tips.SetToolTip(btnRegexCheat, "Quick regex reference card (Ctrl+Shift+X)");
            var btnApiTest = MakeBtn("API Tester", 135, y, Color.FromArgb(0, 100, 80), (s, e) => OpenApiTester());
            tips.SetToolTip(btnApiTest, "Test REST API endpoints (Ctrl+Shift+A)");
            var btnSnippet = MakeBtn("Snippets", 260, y, Color.FromArgb(60, 0, 80), (s, e) => OpenSnippetManager());
            tips.SetToolTip(btnSnippet, "Save and manage code snippets (Ctrl+Shift+Z)");
            var btnTerminal = MakeBtn("Quick Terminal", 385, y, Color.FromArgb(40, 40, 60), (s, e) => OpenQuickTerminal());
            tips.SetToolTip(btnTerminal, "Built-in command terminal (Ctrl+Shift+Q)");
            y += gap;

            var btnColorPicker2 = MakeBtn("Screen Picker", 10, y, Color.FromArgb(140, 60, 0), (s, e) => OpenScreenColorPicker());
            tips.SetToolTip(btnColorPicker2, "Pick any color from screen");
            var btnImgResize = MakeBtn("Image Resize", 135, y, Color.FromArgb(0, 80, 120), (s, e) => OpenImageResizer());
            tips.SetToolTip(btnImgResize, "Resize images in bulk");
            var btnUnitCvt = MakeBtn("Unit Converter", 260, y, Color.FromArgb(60, 80, 0), (s, e) => OpenUnitConverter());
            tips.SetToolTip(btnUnitCvt, "Convert units (length, weight, temp)");
            var btnBaseCvt = MakeBtn("Base Converter", 385, y, Color.FromArgb(0, 60, 80), (s, e) => OpenBaseConverter());
            tips.SetToolTip(btnBaseCvt, "Binary/Octal/Decimal/Hex converter");
            y += gap;

            var btnTextRepl = MakeBtn("Text Replacer", 10, y, Color.FromArgb(100, 40, 60), (s, e) => OpenTextReplacer());
            tips.SetToolTip(btnTextRepl, "Quick text replacement shortcuts");
            var btnFileEnc = MakeBtn("File Encrypt", 135, y, Color.FromArgb(120, 0, 40), (s, e) => OpenFileEncrypt());
            tips.SetToolTip(btnFileEnc, "AES encrypt/decrypt files");
            var btnDisk = MakeBtn("Disk Analyzer", 260, y, Color.FromArgb(40, 80, 60), (s, e) => OpenDiskAnalyzer());
            tips.SetToolTip(btnDisk, "Find largest files on disk");
            var btnCsv = MakeBtn("CSV Viewer", 385, y, Color.FromArgb(80, 60, 20), (s, e) => OpenCsvViewer());
            tips.SetToolTip(btnCsv, "View and parse CSV files");
            y += gap;

            var btnJsonCsv = MakeBtn("JSON->CSV", 10, y, Color.FromArgb(60, 40, 80), (s, e) => OpenJsonToCsv());
            tips.SetToolTip(btnJsonCsv, "Convert JSON arrays to CSV");
            var btnTts = MakeBtn("Word Counter", 135, y, Color.FromArgb(0, 100, 60), (s, e) => OpenWordCounter());
            tips.SetToolTip(btnTts, "Count words, chars, lines, sentences");
            var btnTts2 = MakeBtn("Text to Speech", 260, y, Color.FromArgb(0, 80, 120), (s, e) => OpenTextToSpeech());
            tips.SetToolTip(btnTts2, "Read text aloud with SAPI voice");
            var btnPortScan = MakeBtn("Port Scanner", 385, y, Color.FromArgb(0, 60, 100), (s, e) => OpenPortScanner());
            tips.SetToolTip(btnPortScan, "Scan open ports on host");
            y += gap;

            var btnPwdStr = MakeBtn("Password Check", 10, y, Color.FromArgb(140, 40, 0), (s, e) => OpenPasswordStrength());
            tips.SetToolTip(btnPwdStr, "Check password strength");
            var btnIpGeo = MakeBtn("IP Geolocate", 135, y, Color.FromArgb(80, 0, 80), (s, e) => OpenIpGeo());
            tips.SetToolTip(btnIpGeo, "Location from IP address");
            var btnCurrency = MakeBtn("Currency Cvt", 260, y, Color.FromArgb(100, 60, 20), (s, e) => OpenCurrencyConverter());
            tips.SetToolTip(btnCurrency, "Convert between currencies");
            var btnCharMap = MakeBtn("Character Map", 385, y, Color.FromArgb(60, 60, 40), (s, e) => OpenCharMap());
            tips.SetToolTip(btnCharMap, "Special characters and symbols");
            y += gap;

            var btnWeather = MakeBtn("Weather", 10, y, Color.FromArgb(0, 80, 100), (s, e) => OpenWeather());
            tips.SetToolTip(btnWeather, "Current weather by city");
            y += gap;

            var btnDiff = MakeBtn("Text Diff", 10, y, Color.FromArgb(40, 80, 80), (s, e) => OpenTextDiff());
            tips.SetToolTip(btnDiff, "Compare two texts side by side");
            var btnJsonXml = MakeBtn("JSON->XML", 135, y, Color.FromArgb(80, 40, 40), (s, e) => OpenJsonToXml());
            tips.SetToolTip(btnJsonXml, "Convert JSON to XML format");
            var btnNotes2 = MakeBtn("Notes+", 260, y, Color.FromArgb(60, 80, 40), (s, e) => OpenQuickNotes2());
            tips.SetToolTip(btnNotes2, "Auto-saving sticky notes");
            var btnShred = MakeBtn("File Shredder", 385, y, Color.FromArgb(100, 20, 20), (s, e) => OpenFileShredder());
            tips.SetToolTip(btnShred, "Secure file deletion");
            y += gap;

            var btnPalette = MakeBtn("Palette Gen", 10, y, Color.FromArgb(80, 60, 0), (s, e) => OpenPaletteGen());
            tips.SetToolTip(btnPalette, "Generate color palettes");
            var btnMultiHash = MakeBtn("Multi Hash", 135, y, Color.FromArgb(0, 60, 60), (s, e) => OpenMultiHash());
            tips.SetToolTip(btnMultiHash, "All hashes at once (MD5/SHA)");
            var btnWhois = MakeBtn("DNS Lookup", 260, y, Color.FromArgb(60, 40, 60), (s, e) => OpenWhois());
            tips.SetToolTip(btnWhois, "DNS and IP lookup");
            var btnBarcode = MakeBtn("Barcode Gen", 385, y, Color.FromArgb(40, 60, 80), (s, e) => OpenBarcodeGen());
            tips.SetToolTip(btnBarcode, "Generate barcodes from text");
            y += gap;

            var btnColorHarmony = MakeBtn("Color Harmony", 10, y, Color.FromArgb(80, 40, 80), (s, e) => OpenColorHarmony());
            tips.SetToolTip(btnColorHarmony, "Color wheel harmonies");
            y += gap;

            var btnUninstall = MakeBtn("Uninstall", 10, y, Color.FromArgb(180, 40, 40), (s, e) => OpenUninstallManager());
            tips.SetToolTip(btnUninstall, "Manage installed programs");
            var btnServices = MakeBtn("Services", 135, y, Color.FromArgb(0, 100, 160), (s, e) => OpenServiceManager());
            tips.SetToolTip(btnServices, "View and manage Windows services");
            var btnEnvVars = MakeBtn("Env Vars", 260, y, Color.FromArgb(0, 80, 120), (s, e) => OpenEnvVars());
            tips.SetToolTip(btnEnvVars, "Edit environment variables");
            var btnHosts = MakeBtn("Hosts", 385, y, Color.FromArgb(60, 60, 80), (s, e) => OpenHostsEditor());
            tips.SetToolTip(btnHosts, "Edit the hosts file");
            y += gap;

            var btnPowerPlans = MakeBtn("Power Plans", 10, y, Color.FromArgb(100, 60, 0), (s, e) => OpenPowerPlans());
            tips.SetToolTip(btnPowerPlans, "Manage power plans");
            var btnTasks = MakeBtn("Tasks", 135, y, Color.FromArgb(0, 80, 80), (s, e) => OpenScheduledTasks());
            tips.SetToolTip(btnTasks, "View scheduled tasks");
            var btnDnsChanger = MakeBtn("DNS Changer", 260, y, Color.FromArgb(80, 40, 80), (s, e) => OpenDnsChanger());
            tips.SetToolTip(btnDnsChanger, "Change DNS servers quickly");
            var btnNetConns = MakeBtn("Net Connections", 385, y, Color.FromArgb(0, 100, 60), (s, e) => OpenNetConnections());
            tips.SetToolTip(btnNetConns, "View active network connections");
            y += gap;

            var btnTraceroute = MakeBtn("Traceroute", 10, y, Color.FromArgb(40, 80, 60), (s, e) => OpenTraceroute());
            tips.SetToolTip(btnTraceroute, "Trace route to a host");
            var btnIpConfig = MakeBtn("IP Config", 135, y, Color.FromArgb(80, 80, 0), (s, e) => OpenIpConfig());
            tips.SetToolTip(btnIpConfig, "View IP configuration");
            var btnFirewall = MakeBtn("Firewall", 260, y, Color.FromArgb(120, 40, 0), (s, e) => OpenFirewallRules());
            tips.SetToolTip(btnFirewall, "Manage firewall rules");
            var btnBandwidth = MakeBtn("Bandwidth", 385, y, Color.FromArgb(0, 60, 100), (s, e) => OpenBandwidthTest());
            tips.SetToolTip(btnBandwidth, "Test network bandwidth");
            y += gap;

            var btnWinSpy = MakeBtn("Window Spy", 10, y, Color.FromArgb(100, 0, 80), (s, e) => OpenWindowInspector());
            tips.SetToolTip(btnWinSpy, "Inspect window properties");
            var btnRuler = MakeBtn("Ruler", 135, y, Color.FromArgb(60, 40, 100), (s, e) => OpenScreenRuler());
            tips.SetToolTip(btnRuler, "Measure screen distances");
            var btnProcWatch = MakeBtn("Process Watch", 260, y, Color.FromArgb(0, 120, 60), (s, e) => OpenProcessWatcher());
            tips.SetToolTip(btnProcWatch, "Real-time process monitor");
            var btnQuickLaunch = MakeBtn("Quick Launch", 385, y, Color.FromArgb(80, 60, 40), (s, e) => OpenQuickLauncher());
            tips.SetToolTip(btnQuickLaunch, "Launch apps quickly");
            y += gap;

            var btnAlwaysTop = MakeBtn("Always Top", 10, y, Color.FromArgb(40, 80, 100), (s, e) => OpenAlwaysOnTop());
            tips.SetToolTip(btnAlwaysTop, "Toggle always-on-top for windows");
            var btnDiskHealth = MakeBtn("Disk Health", 135, y, Color.FromArgb(0, 100, 40), (s, e) => OpenDiskHealth());
            tips.SetToolTip(btnDiskHealth, "Check disk S.M.A.R.T. status");
            var btnGpu = MakeBtn("GPU Monitor", 260, y, Color.FromArgb(60, 0, 100), (s, e) => OpenGpuMonitor());
            tips.SetToolTip(btnGpu, "Monitor GPU usage and temp");
            var btnBattery = MakeBtn("Battery", 385, y, Color.FromArgb(100, 80, 0), (s, e) => OpenBatteryReport());
            tips.SetToolTip(btnBattery, "Battery health and report");
            y += gap;

            var btnSysInfoPro = MakeBtn("Sys Info Pro", 10, y, Color.FromArgb(0, 80, 60), (s, e) => OpenSystemInfoPro());
            tips.SetToolTip(btnSysInfoPro, "Detailed system information");
            var btnDiskSpeed = MakeBtn("Disk Speed", 135, y, Color.FromArgb(80, 0, 60), (s, e) => OpenDiskBenchmark());
            tips.SetToolTip(btnDiskSpeed, "Benchmark disk read/write speed");
            var btnOcr = MakeBtn("Screenshot OCR", 260, y, Color.FromArgb(40, 60, 80), (s, e) => OpenScreenshotOcr());
            tips.SetToolTip(btnOcr, "Extract text from screenshots");
            var btnLockCheck = MakeBtn("Lock Check", 385, y, Color.FromArgb(100, 40, 40), (s, e) => OpenFileLocksmith());
            tips.SetToolTip(btnLockCheck, "Find which process locks a file");
            y += gap;

            var btnClipMon = MakeBtn("Clip Monitor", 10, y, Color.FromArgb(0, 100, 100), (s, e) => OpenClipboardMonitor());
            tips.SetToolTip(btnClipMon, "Monitor clipboard changes");
            var btnSleepTimer = MakeBtn("Sleep Timer", 135, y, Color.FromArgb(80, 40, 60), (s, e) => OpenSleepTimer());
            tips.SetToolTip(btnSleepTimer, "Schedule PC sleep timer");
            var btnCrapware = MakeBtn("Crapware", 260, y, Color.FromArgb(120, 60, 0), (s, e) => OpenCrapwareDetector());
            tips.SetToolTip(btnCrapware, "Detect and remove bloatware");
            var btnStats = MakeBtn("Stats", 385, y, Color.FromArgb(40, 80, 60), (s, e) => ShowStats());
            tips.SetToolTip(btnStats, "View tool usage statistics");
            y += gap + 10;

            statusLabel = new Label { Text = "Ready", Font = statusFont, ForeColor = Color.FromArgb(60, 60, 80), AutoSize = true, Location = new Point(20, y + 5) };
            var footer = new Label { Text = "Developed by nu1lbyte", Font = footFont, ForeColor = Color.FromArgb(50, 50, 60), AutoSize = true, Location = new Point(175, y + 10) };

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.Control && e.Shift && e.KeyCode == Keys.W) OpenFirewallRules();
                if (e.Control && e.Shift && e.KeyCode == Keys.R) OpenDnsChanger();
                if (e.Control && e.Shift && e.KeyCode == Keys.Q) OpenTraceroute();
                if (e.Control && e.Shift && e.KeyCode == Keys.A) OpenAlwaysOnTop();
                if (e.Control && !e.Shift && e.KeyCode == Keys.L) Lock();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D) CleanTemp();
                if (e.Control && e.Shift && e.KeyCode == Keys.D) ToggleDark();
                if (e.Control && !e.Shift && e.KeyCode == Keys.S) Screenshot();
                if (e.Control && e.Shift && e.KeyCode == Keys.S) ScreenshotToClipboard();
                if (e.Control && !e.Shift && e.KeyCode == Keys.P) StartPingOverlay();
                if (e.Control && !e.Shift && e.KeyCode == Keys.F) { ToggleFocus(); UpdateFocusBtn(); }
                if (e.Control && e.Shift && e.KeyCode == Keys.F) { ToggleFocus(); UpdateFocusBtn(); }
                if (e.Control && !e.Shift && e.KeyCode == Keys.M) ToggleMic();
                if (e.Control && !e.Shift && e.KeyCode == Keys.V) EnableClip();
                if (e.Control && !e.Shift && e.KeyCode == Keys.R) LaunchApps();
                if (e.Control && !e.Shift && e.KeyCode == Keys.U) StartVolumeControl();
                if (e.Control && !e.Shift && e.KeyCode == Keys.N) ShowNetworkInfo();
                if (e.Control && !e.Shift && e.KeyCode == Keys.I) ShowSystemInfo();
                if (e.Control && !e.Shift && e.KeyCode == Keys.B) EmptyRecycleBin();
                if (e.Control && !e.Shift && e.KeyCode == Keys.T) ShowProcesses();
                if (e.Control && e.Shift && e.KeyCode == Keys.L) { string r = LayoutWork(); MessageBox.Show(r, "GM"); }
                if (e.Control && e.Shift && e.KeyCode == Keys.C) OpenQuickCmd();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D1) OpenTimer();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D2) OpenColorPicker();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D3) OpenHistoryCleaner();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D4) OpenShutdownTimer();
                if (e.Control && !e.Shift && e.KeyCode == Keys.D5) OpenMatrixRain();
                if (e.Control && !e.Shift && e.KeyCode == Keys.Q) CleanupAndExit();
                if (e.Control && !e.Shift && e.KeyCode == Keys.H) OpenFileHash();
                if (e.Control && !e.Shift && e.KeyCode == Keys.W) ShowWifiPasswords();
                if (e.Control && e.Shift && e.KeyCode == Keys.M) OpenCpuMonitor();
                if (e.Control && e.Shift && e.KeyCode == Keys.N) OpenQuickNotes();
                if (e.Control && e.Shift && e.KeyCode == Keys.B) OpenBulkRenamer();
                if (e.Control && e.Shift && e.KeyCode == Keys.P) OpenProcessPriority();
                if (e.Control && !e.Shift && e.KeyCode == Keys.O) OpenBase64();
                if (e.Control && !e.Shift && e.KeyCode == Keys.G) OpenPasswordGen();
                if (e.Control && !e.Shift && e.KeyCode == Keys.E) OpenFileInfo();
                if (e.Control && !e.Shift && e.KeyCode == Keys.X) OpenQuickClean();
                if (e.Control && !e.Shift && e.KeyCode == Keys.Y) ShowPublicIP();
                if (e.Control && !e.Shift && e.KeyCode == Keys.J) OpenJsonFormatter();
                if (e.Control && e.Shift && e.KeyCode == Keys.T) OpenTextHash();
                if (e.Control && !e.Shift && e.KeyCode == Keys.K) OpenClipboardManager();
                if (e.Control && e.Shift && e.KeyCode == Keys.I) OpenDriveInfo();
                if (e.Control && !e.Shift && e.KeyCode == Keys.Z) OpenCalculator();
                if (e.Control && e.Shift && e.KeyCode == Keys.H) OpenHexViewer();
                if (e.Control && e.Shift && e.KeyCode == Keys.Y) OpenCodeFormatter();
                if (e.Control && e.Shift && e.KeyCode == Keys.O) OpenLoremIpsum();
                if (e.Control && e.Shift && e.KeyCode == Keys.E) OpenTimestampConverter();
                if (e.Control && e.Shift && e.KeyCode == Keys.G) OpenCssGradient();
                if (e.Control && e.Shift && e.KeyCode == Keys.X) OpenRegexCheat();
                if (e.Control && e.Shift && e.KeyCode == Keys.Z) OpenSnippetManager();
                if (e.Control && e.Alt && e.KeyCode == Keys.C) OpenScreenColorPicker();
                if (e.Control && e.Alt && e.KeyCode == Keys.R) OpenImageResizer();
                if (e.Control && e.Alt && e.KeyCode == Keys.U) OpenUnitConverter();
                if (e.Control && e.Alt && e.KeyCode == Keys.B) OpenBaseConverter();
                if (e.Control && e.Alt && e.KeyCode == Keys.T) OpenTextReplacer();
                if (e.Control && e.Alt && e.KeyCode == Keys.E) OpenFileEncrypt();
                if (e.Control && e.Alt && e.KeyCode == Keys.D) OpenDiskAnalyzer();
                if (e.Control && e.Alt && e.KeyCode == Keys.V) OpenCsvViewer();
                if (e.Control && e.Alt && e.KeyCode == Keys.J) OpenJsonToCsv();
                if (e.Control && e.Alt && e.KeyCode == Keys.S) OpenTextToSpeech();
                if (e.Control && e.Alt && e.KeyCode == Keys.P) OpenPortScanner();
                if (e.Control && e.Alt && e.KeyCode == Keys.W) OpenWeather();
                if (e.Control && e.Alt && e.KeyCode == Keys.N) OpenQuickNotes2();
                if (e.Control && e.Alt && e.KeyCode == Keys.F) OpenFileShredder();
                if (e.Control && e.Shift && e.KeyCode == Keys.U) OpenUninstallManager();
                if (e.Control && e.Shift && e.KeyCode == Keys.V) OpenEnvVars();
                if (e.KeyCode == Keys.F3) { txtSearch.Focus(); txtSearch.SelectAll(); }
            };

            this.Controls.AddRange(new Control[] { title, sub, txtSearch, btnClearSearch, btnRefreshAll, lblRecent, recentPanel, lblFav, favPanel, btnLock, btnClean, btnDark, btnMinAll, btnMute, btnFocus, btnClip, btnCmd, btnSnap, btnRecord, btnLayout, btnAbout, btnStats, btnAfk, btnVol, btnNet, btnSysInfo, btnRecycle, btnProcesses, btnTimer, btnColorPick, btnHistClean, btnShutTimer, btnMatrix, btnFileHash, btnCpu, btnWifi, btnNotes, btnRenamer, btnBase64, btnPassGen, btnProcPri, btnStartup, btnQClean, btnPubIP, btnFileInfo, btnScreenTimer, btnTextTools, btnColorPal, btnCalc, btnUrlEnc, btnJson, btnRegex, btnTextHash, btnClipMgr, btnDriveInfo, btnPaint, btnQr, btnNetSpeed, btnHexView, btnCodeFmt, btnLorem, btnTimestamp, btnMarkdown, btnCssGrad, btnRegexCheat, btnApiTest, btnSnippet, btnTerminal, btnColorPicker2, btnImgResize, btnUnitCvt, btnBaseCvt, btnTextRepl, btnFileEnc, btnDisk, btnCsv, btnJsonCsv, btnTts, btnTts2, btnPwdStr, btnPortScan, btnIpGeo, btnWeather, btnCurrency, btnCharMap, btnDiff, btnJsonXml, btnNotes2, btnShred, btnPalette, btnMultiHash, btnWhois, btnBarcode, btnColorHarmony, btnUninstall, btnServices, btnEnvVars, btnHosts, btnPowerPlans, btnTasks, btnDnsChanger, btnNetConns, btnTraceroute, btnIpConfig, btnFirewall, btnBandwidth, btnWinSpy, btnRuler, btnProcWatch, btnQuickLaunch, btnAlwaysTop, btnDiskHealth, btnGpu, btnBattery, btnSysInfoPro, btnDiskSpeed, btnOcr, btnLockCheck, btnClipMon, btnSleepTimer, btnCrapware, statusLabel, footer });
            statusRef = statusLabel;
            UpdateFocusBtn();
        }

        void UpdateFocusBtn()
        {
            if (focusActive)
            {
                btnFocus.BackColor = Color.FromArgb(0, 180, 0);
                btnFocus.Tag = Color.FromArgb(0, 180, 0);
                btnFocus.Text = "Focus: ON";
            }
            else
            {
                btnFocus.BackColor = Color.FromArgb(200, 120, 0);
                btnFocus.Tag = Color.FromArgb(200, 120, 0);
                btnFocus.Text = "Focus Mode";
            }
        }

        void CleanupAndExit()
        {
            if (exiting) return;
            exiting = true;
            if (focusActive)
            {
                try
                {
                    if (File.Exists(hostsBackup)) File.Copy(hostsBackup, hostsPath, true);
                    else File.WriteAllText(hostsPath, "");
                    FlushDns();
                }
                catch { }
            }
            foreach (Form o in overlays.ToArray())
            {
                try { o.Close(); o.Dispose(); } catch { }
            }
            foreach (Control c in this.Controls)
            {
                try { if (c.ContextMenuStrip != null) { c.ContextMenuStrip.Dispose(); c.ContextMenuStrip = null; } } catch { }
            }
            if (focusTimer != null) { focusTimer.Stop(); focusTimer.Dispose(); focusTimer = null; }
            tray.Visible = false;
            if (tray.ContextMenuStrip != null) { tray.ContextMenuStrip.Dispose(); tray.ContextMenuStrip = null; }
            tray.Dispose(); tray = null;
            tips.Dispose(); tips = null;
            titleFont.Dispose(); titleFont = null;
            subFont.Dispose(); subFont = null;
            btnFont.Dispose(); btnFont = null;
            footFont.Dispose(); footFont = null;
            formFont.Dispose(); formFont = null;
            statusFont.Dispose(); statusFont = null;
            if (lblFavFont != null) { lblFavFont.Dispose(); lblFavFont = null; }
            if (recentFont != null) { recentFont.Dispose(); recentFont = null; }
            if (recentPanel != null)
            {
                foreach (Control c in recentPanel.Controls) { try { if (c.Font != null) c.Font.Dispose(); } catch { } }
                recentPanel.Dispose(); recentPanel = null;
            }
            if (txtSearch != null) { try { txtSearch.Font.Dispose(); } catch { } }
            if (btnClearSearch != null) { try { btnClearSearch.Font.Dispose(); } catch { } }
            if (favPanel != null)
            {
                foreach (Control c in favPanel.Controls) { try { if (c.Font != null) c.Font.Dispose(); } catch { } }
                favPanel.Dispose(); favPanel = null;
            }
            Application.Exit();
        }

        void ShowProcesses()
        {
            try
            {
                var f = new Form();
                f.Text = "GM - Processes";
                f.Size = new Size(550, 600);
                f.FormBorderStyle = FormBorderStyle.FixedSingle;
                f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.BackColor = Color.FromArgb(15, 15, 25);
                f.Icon = this.Icon;

                var searchBox = new TextBox();
                searchBox.Font = new Font("Segoe UI", 9);
                searchBox.BackColor = Color.FromArgb(30, 30, 40);
                searchBox.ForeColor = Color.White;
                searchBox.Dock = DockStyle.Top;
                searchBox.Height = 28;
                searchBox.Text = "";

                var list = new ListBox();
                list.Font = new Font("Consolas", 9);
                list.BackColor = Color.FromArgb(15, 15, 25);
                list.ForeColor = Color.FromArgb(0, 200, 100);
                list.Dock = DockStyle.Fill;
                list.SelectionMode = SelectionMode.One;

                var panel = new Panel();
                panel.Dock = DockStyle.Bottom;
                panel.Height = 45;
                panel.BackColor = Color.FromArgb(20, 20, 30);

                var btnKill = new Button { Text = "Kill", Location = new Point(10, 8), Size = new Size(60, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(180, 30, 30), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
                btnKill.FlatAppearance.BorderSize = 0;
                var btnRefresh = new Button { Text = "Refresh", Location = new Point(80, 8), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
                btnRefresh.FlatAppearance.BorderSize = 0;
                var btnCopyList = new Button { Text = "Copy", Location = new Point(160, 8), Size = new Size(60, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
                btnCopyList.FlatAppearance.BorderSize = 0;
                var btnExport = new Button { Text = "Export", Location = new Point(230, 8), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 0), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
                btnExport.FlatAppearance.BorderSize = 0;
                var lblInfo = new Label { Text = "", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(100, 100, 120), AutoSize = true, Location = new Point(310, 14) };

                Action refreshProcs = null;
                refreshProcs = () =>
                {
                    list.Items.Clear();
                    string filter = searchBox.Text.Trim().ToLower();
                    var procs = Process.GetProcesses();
                    var sorted = new List<Process>();
                    foreach (var p in procs) { try { sorted.Add(p); } catch { try { p.Dispose(); } catch { } } }
                    sorted.Sort((a, b) => { try { return b.WorkingSet64.CompareTo(a.WorkingSet64); } catch { return 0; } });
                    int shown = 0;
                    foreach (var p in sorted)
                    {
                        try
                        {
                            string name = p.ProcessName.Length > 26 ? p.ProcessName.Substring(0, 23) + "..." : p.ProcessName;
                            string entry = String.Format("{0,-28} PID:{1,-8} {2,6} MB", name, p.Id, p.WorkingSet64 / 1024 / 1024);
                            if (filter.Length == 0 || p.ProcessName.ToLower().Contains(filter) || p.Id.ToString().Contains(filter))
                            {
                                list.Items.Add(entry);
                                shown++;
                            }
                        }
                        catch { }
                    }
                    long totalMem = 0;
                    foreach (var p in procs) { try { totalMem += p.WorkingSet64; } catch { } finally { try { p.Dispose(); } catch { } } }
                    lblInfo.Text = String.Format("{0}/{1} processes | {2} MB total", shown, sorted.Count, totalMem / 1024 / 1024);
                };

                searchBox.TextChanged += (s2, e2) => refreshProcs();
                refreshProcs();
                btnRefresh.Click += (s2, e2) => refreshProcs();
                btnKill.Click += (s2, e2) =>
                {
                    if (list.SelectedIndex < 0 || list.SelectedItem == null) { MessageBox.Show("Select a process first.", "GM"); return; }
                    string selected = list.SelectedItem.ToString();
                    int pidStart = selected.IndexOf("PID:");
                    int pidEnd = selected.IndexOf(" ", pidStart + 4);
                    if (pidStart < 0 || pidEnd < 0) return;
                    int pid;
                    if (!int.TryParse(selected.Substring(pidStart + 4, pidEnd - pidStart - 4), out pid)) return;
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        var currentProc = Process.GetCurrentProcess();
                        try
                        {
                            if (proc.Id == currentProc.Id) { MessageBox.Show("Cannot kill GM itself.", "GM"); return; }
                            string msg = "Kill " + proc.ProcessName + " (PID " + pid + ")?";
                            if (MessageBox.Show(msg, "GM", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            {
                                proc.Kill();
                                SetStatus("Killed: " + proc.ProcessName);
                                refreshProcs();
                            }
                        }
                        finally { try { proc.Dispose(); } catch { } try { currentProc.Dispose(); } catch { } }
                    }
                    catch { MessageBox.Show("Could not kill process.", "GM"); }
                };

                btnCopyList.Click += (s2, e2) =>
                {
                    try
                    {
                        string text = "";
                        foreach (var item in list.Items) text += item.ToString() + "\n";
                        Clipboard.SetText(text);
                        MessageBox.Show("Copied to clipboard.", "GM");
                    }
                    catch { }
                };

                btnExport.Click += (s2, e2) =>
                {
                    try
                    {
                        string path = PromptSavePath("GM - Export Processes", "Text files", "gm_processes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                        if (path != null)
                        {
                            string text = lblInfo.Text + "\n\n";
                            foreach (var item in list.Items) text += item.ToString() + "\n";
                            File.WriteAllText(path, text);
                            MessageBox.Show("Exported to:\n" + path, "GM");
                        }
                    }
                    catch { MessageBox.Show("Export failed.", "GM"); }
                };

                panel.Controls.AddRange(new Control[] { btnKill, btnRefresh, btnCopyList, btnExport, lblInfo });
                f.Controls.Add(list);
                f.Controls.Add(searchBox);
                f.Controls.Add(panel);
                f.FormClosed += (s, e) => { searchBox.Font.Dispose(); list.Font.Dispose(); btnKill.Font.Dispose(); btnRefresh.Font.Dispose(); btnCopyList.Font.Dispose(); btnExport.Font.Dispose(); lblInfo.Font.Dispose(); };
                f.Show();
                SetStatus("Processes displayed");
            }
            catch { MessageBox.Show("Could not list processes.", "GM"); }
        }

        void OpenQuickCmd()
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe");
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Process.Start(psi);
                SetStatus("Command Prompt opened");
            }
            catch { MessageBox.Show("Could not open Command Prompt.", "GM"); }
        }

        void ShowAbout()
        {
            string msg = "GM Command Center v2.9\n\n";
            msg += "Developed by nu1lbyte\n\n";
            msg += "Features (110 tools):\n";
            msg += "Lock, Clean, Dark Mode, Min All, Mute Mic\n";
            msg += "Focus Mode, Clipboard, Quick CMD, Screenshot\n";
            msg += "Ping Overlay, Layout, AFK Launch, Volume\n";
            msg += "Network, System Info, Empty Bin, Processes\n";
            msg += "CPU Monitor, WiFi Passwords, Quick Notes\n";
            msg += "File Hash, Bulk Renamer, Base64, Password Gen\n";
            msg += "Process Priority, Startup Mgr, Quick Clean\n";
            msg += "Public IP, File Info, Screen Timer, Text Tools\n";
            msg += "Color Palette, Calculator, URL Encoder\n";
            msg += "JSON Format, Regex Test, Text Hash\n";
            msg += "Clipboard Mgr, Drive Info, Quick Paint\n";
            msg += "Pattern Code, Net Speed, Hex Viewer\n";
            msg += "Code Format, Lorem Ipsum, Timestamp\n";
            msg += "Markdown, CSS Gradient, Regex Cheat\n";
            msg += "API Tester, Snippets, Quick Terminal\n";
            msg += "Color Picker, Image Resize, Unit Converter\n";
            msg += "Base Converter, Text Replacer, File Encrypt\n";
            msg += "Disk Analyzer, CSV Viewer, JSON->CSV\n";
            msg += "Word Counter, Text to Speech, Password Check\n";
            msg += "Port Scanner\n";
            msg += "IP Geolocate, Weather, Currency Converter\n";
            msg += "Character Map, Text Diff, JSON->XML\n";
            msg += "Quick Notes, File Shredder, Palette Gen\n";
            msg += "Multi Hash, DNS Lookup, Barcode Gen\n";
            msg += "Color Harmony\n\n";
            msg += "Embedded Tools (Ctrl+1 to 5):\n";
            msg += "Ctrl+1=Timer, Ctrl+2=ColorPicker\n";
            msg += "Ctrl+3=HistoryClean, Ctrl+4=ShutdownTimer\n";
            msg += "Ctrl+5=MatrixRain\n\n";
            msg += "Right-click any button to pin favourites!\n\n";
            msg += "Developed by nu1lbyte - " + DateTime.Now.Year;
            MessageBox.Show(msg, "About GM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowStats()
        {
            var f = new Form();
            f.Text = "GM - Tool Statistics";
            f.Size = new Size(480, 560);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var lblTitle = new Label { Text = "Tool Usage Statistics", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.FromArgb(0, 170, 255), AutoSize = true, Location = new Point(120, 10) };

            var lblSession = new Label { Text = "Session duration: " + (DateTime.Now - sessionStart).ToString(@"hh\:mm\:ss"), Font = new Font("Consolas", 10), ForeColor = Color.Lime, AutoSize = true, Location = new Point(20, 40) };

            int totalCount = 0;
            foreach (var kv in toolUsage) totalCount += kv.Value;
            var lblTotal = new Label { Text = "Total tools opened: " + totalCount, Font = new Font("Consolas", 10), ForeColor = Color.Cyan, AutoSize = true, Location = new Point(20, 62) };

            var sorted = new List<KeyValuePair<string, int>>(toolUsage);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            int topCount = Math.Min(sorted.Count, 10);

            var panel = new Panel { Location = new Point(10, 90), Size = new Size(445, 380), AutoScroll = true, BackColor = Color.FromArgb(20, 20, 35) };

            int maxVal = topCount > 0 ? sorted[0].Value : 1;
            int barY = 5;
            for (int i = 0; i < topCount; i++)
            {
                string name = sorted[i].Key;
                int count = sorted[i].Value;

                var lblRank = new Label
                {
                    Text = (i + 1) + ".",
                    Font = new Font("Segoe UI", 8, FontStyle.Bold),
                    ForeColor = Color.FromArgb(150, 150, 170),
                    AutoSize = true,
                    Location = new Point(5, barY + 2)
                };

                var lblName = new Label
                {
                    Text = name,
                    Font = new Font("Segoe UI", 8, FontStyle.Bold),
                    ForeColor = Color.White,
                    AutoSize = true,
                    Location = new Point(25, barY + 2)
                };

                int barWidth = maxVal > 0 ? (int)((double)count / maxVal * 300) : 10;
                if (barWidth < 10) barWidth = 10;

                var bar = new Label
                {
                    Size = new Size(barWidth, 16),
                    Location = new Point(140, barY),
                    BackColor = Color.FromArgb(0, 120, 80)
                };

                var lblCount = new Label
                {
                    Text = count.ToString(),
                    Font = new Font("Consolas", 8, FontStyle.Bold),
                    ForeColor = Color.Lime,
                    AutoSize = true,
                    Location = new Point(140 + barWidth + 6, barY + 1)
                };

                panel.Controls.Add(lblRank);
                panel.Controls.Add(lblName);
                panel.Controls.Add(bar);
                panel.Controls.Add(lblCount);
                barY += 35;
            }

            if (topCount == 0)
            {
                var lblEmpty = new Label { Text = "No tool usage data yet.", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(80, 80, 100), AutoSize = true, Location = new Point(140, 30) };
                panel.Controls.Add(lblEmpty);
            }

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var btnClose = new Button { Text = "Close", Location = new Point(190, 485), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = btnFont, Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => f.Close();

            f.Controls.AddRange(new Control[] { lblTitle, lblSession, lblTotal, panel, btnClose });
            f.FormClosed += (s, e) => { lblTitle.Font.Dispose(); lblSession.Font.Dispose(); lblTotal.Font.Dispose(); btnFont.Dispose(); try { f.Icon.Dispose(); } catch { } };
            f.Show();
            SetStatus("Tool Statistics opened");
        }

        Button MakeBtn(string text, int x, int y, Color bg, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(115, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = btnFont,
                Cursor = Cursors.Hand,
                Tag = text
            };
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (s, e) => { b.BackColor = Color.FromArgb(Math.Min(bg.R + 30, 255), Math.Min(bg.G + 30, 255), Math.Min(bg.B + 30, 255)); string tipText = tips.GetToolTip(b); SetStatus("Tool: " + text + (tipText.Length > 0 ? " \u2014 " + tipText : "")); };
            b.MouseLeave += (s, e) => { b.BackColor = bg; SetStatus("Ready"); };
            b.Click += (s, e) => { TrackUsage(text); AddToRecent(text); if (recentPanel != null) RefreshRecent(recentPanel, recentFont); onClick(s, e); };
            var ctx = new ContextMenuStrip();
            string btnName = text;
            var favItem = new ToolStripMenuItem(IsFavourite(btnName) ? "Remove from Favourites" : "Add to Favourites");
            favItem.Click += (s, e) =>
            {
                ToggleFavourite(btnName);
                favItem.Text = IsFavourite(btnName) ? "Remove from Favourites" : "Add to Favourites";
                RefreshFavourites(favPanel);
            };
            ctx.Items.Add(favItem);
            b.ContextMenuStrip = ctx;
            allToolButtons.Add(b);
            return b;
        }

        void OpenUninstallManager()
        {
            var f = new Form();
            f.Text = "Uninstall Manager";
            f.Size = new Size(750, 520);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var grid = new DataGridView();
            grid.Font = new Font("Consolas", 9);
            grid.BackColor = Color.FromArgb(20, 20, 35);
            grid.ForeColor = Color.White;
            grid.GridColor = Color.FromArgb(40, 40, 60);
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            grid.Dock = DockStyle.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.FromArgb(20, 20, 35);

            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("Version", "Version");
            grid.Columns.Add("Publisher", "Publisher");
            grid.Columns.Add("InstallDate", "InstallDate");

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Copy Name", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count > 0)
                {
                    try { Clipboard.SetText(grid.SelectedRows[0].Cells["Name"].Value.ToString()); } catch { }
                }
            });
            grid.ContextMenuStrip = ctx;

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnUninstall = new Button();
            btnUninstall.Text = "Uninstall";
            btnUninstall.Location = new Point(10, 8);
            btnUninstall.Size = new Size(100, 32);
            btnUninstall.FlatStyle = FlatStyle.Flat;
            btnUninstall.BackColor = Color.FromArgb(200, 40, 40);
            btnUninstall.ForeColor = Color.White;
            btnUninstall.Font = btnFont;
            btnUninstall.Cursor = Cursors.Hand;
            btnUninstall.FlatAppearance.BorderSize = 0;
            btnUninstall.Click += (s2, e2) =>
            {
                try { Process.Start(new ProcessStartInfo("control.exe", "appwiz.cpl") { UseShellExecute = true }); }
                catch { }
            };

            var btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new Point(120, 8);
            btnRefresh.Size = new Size(80, 32);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(0, 100, 160);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = btnFont;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(650, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            var lblInfo = new Label();
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(210, 15);

            Action loadApps = () =>
            {
                grid.Rows.Clear();
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false))
                    {
                        if (key != null)
                        {
                            foreach (string subKeyName in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var subKey = key.OpenSubKey(subKeyName))
                                    {
                                        if (subKey == null) continue;
                                        string name = subKey.GetValue("DisplayName") != null ? subKey.GetValue("DisplayName").ToString() : "";
                                        if (name.Length == 0) continue;
                                        string version = subKey.GetValue("DisplayVersion") != null ? subKey.GetValue("DisplayVersion").ToString() : "";
                                        string publisher = subKey.GetValue("Publisher") != null ? subKey.GetValue("Publisher").ToString() : "";
                                        string installDate = subKey.GetValue("InstallDate") != null ? subKey.GetValue("InstallDate").ToString() : "";
                                        grid.Rows.Add(name, version, publisher, installDate);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
                lblInfo.Text = grid.Rows.Count + " programs found";
            };

            btnRefresh.Click += (s2, e2) => loadApps();
            loadApps();

            grid.DoubleClick += (s2, e2) =>
            {
                try { Process.Start(new ProcessStartInfo("control.exe", "appwiz.cpl") { UseShellExecute = true }); }
                catch { }
            };

            panel.Controls.AddRange(new Control[] { btnUninstall, btnRefresh, btnClose, lblInfo });
            f.Controls.Add(grid);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { grid.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); ctx.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Uninstall Manager closed");
        }

        void OpenServiceManager()
        {
            var f = new Form();
            f.Text = "Service Manager";
            f.Size = new Size(700, 520);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var grid = new DataGridView();
            grid.Font = new Font("Consolas", 9);
            grid.BackColor = Color.FromArgb(20, 20, 35);
            grid.ForeColor = Color.White;
            grid.GridColor = Color.FromArgb(40, 40, 60);
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            grid.Dock = DockStyle.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.FromArgb(20, 20, 35);

            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("DisplayName", "Display Name");
            grid.Columns.Add("Status", "Status");
            grid.Columns.Add("StartType", "Start Type");

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new Point(10, 8);
            btnRefresh.Size = new Size(80, 32);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(0, 100, 160);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = btnFont;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(610, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            var lblInfo = new Label();
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(100, 15);

            Action loadServices = () =>
            {
                grid.Rows.Clear();
                try
                {
                    var services = System.ServiceProcess.ServiceController.GetServices();
                    foreach (var svc in services)
                    {
                        try
                        {
                            string startType = "";
                            grid.Rows.Add(svc.ServiceName, svc.DisplayName, svc.Status.ToString(), startType);
                        }
                        catch { }
                        finally { svc.Dispose(); }
                    }
                    lblInfo.Text = grid.Rows.Count + " services";
                }
                catch { lblInfo.Text = "Error loading services"; }
            };

            var ctx = new ContextMenuStrip();
            var miStart = ctx.Items.Add("Start", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string svcName = grid.SelectedRows[0].Cells["Name"].Value.ToString();
                try
                {
                    using (var svc = new System.ServiceProcess.ServiceController(svcName))
                    {
                        svc.Start();
                        svc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                    lblInfo.Text = "Started: " + svcName;
                    loadServices();
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            var miStop = ctx.Items.Add("Stop", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string svcName = grid.SelectedRows[0].Cells["Name"].Value.ToString();
                try
                {
                    using (var svc = new System.ServiceProcess.ServiceController(svcName))
                    {
                        svc.Stop();
                        svc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                    lblInfo.Text = "Stopped: " + svcName;
                    loadServices();
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            var miRestart = ctx.Items.Add("Restart", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string svcName = grid.SelectedRows[0].Cells["Name"].Value.ToString();
                try
                {
                    using (var svc = new System.ServiceProcess.ServiceController(svcName))
                    {
                        svc.Stop();
                        svc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        svc.Start();
                        svc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                    lblInfo.Text = "Restarted: " + svcName;
                    loadServices();
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            grid.ContextMenuStrip = ctx;
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(100, 15);

            grid.CellContextMenuStripNeeded += (s2, e2) =>
            {
                if (e2.RowIndex >= 0 && e2.RowIndex < grid.Rows.Count)
                {
                    grid.ClearSelection();
                    grid.Rows[e2.RowIndex].Selected = true;
                    string status = grid.Rows[e2.RowIndex].Cells["Status"].Value.ToString();
                    bool isRunning = status == "Running";
                    bool isStopped = status == "Stopped";
                    miStart.Enabled = isStopped;
                    miStop.Enabled = isRunning;
                    miRestart.Enabled = isRunning;
                }
            };

            btnRefresh.Click += (s2, e2) => loadServices();
            loadServices();

            panel.Controls.AddRange(new Control[] { btnRefresh, btnClose, lblInfo });
            f.Controls.Add(grid);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { grid.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); ctx.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Service Manager closed");
        }

        void OpenEnvVars()
        {
            var f = new Form();
            f.Text = "Environment Variables";
            f.Size = new Size(650, 520);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9);

            var tabUser = new TabPage("User");
            var tabSystem = new TabPage("System");

            var gridUser = new DataGridView();
            gridUser.Font = new Font("Consolas", 9);
            gridUser.BackColor = Color.FromArgb(20, 20, 35);
            gridUser.ForeColor = Color.White;
            gridUser.GridColor = Color.FromArgb(40, 40, 60);
            gridUser.BorderStyle = BorderStyle.None;
            gridUser.AllowUserToAddRows = false;
            gridUser.ReadOnly = true;
            gridUser.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            gridUser.Dock = DockStyle.Fill;
            gridUser.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridUser.MultiSelect = false;
            gridUser.BackgroundColor = Color.FromArgb(20, 20, 35);
            gridUser.Columns.Add("Name", "Name");
            gridUser.Columns.Add("Value", "Value");

            var gridSystem = new DataGridView();
            gridSystem.Font = new Font("Consolas", 9);
            gridSystem.BackColor = Color.FromArgb(20, 20, 35);
            gridSystem.ForeColor = Color.White;
            gridSystem.GridColor = Color.FromArgb(40, 40, 60);
            gridSystem.BorderStyle = BorderStyle.None;
            gridSystem.AllowUserToAddRows = false;
            gridSystem.ReadOnly = true;
            gridSystem.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            gridSystem.Dock = DockStyle.Fill;
            gridSystem.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridSystem.MultiSelect = false;
            gridSystem.BackgroundColor = Color.FromArgb(20, 20, 35);
            gridSystem.Columns.Add("Name", "Name");
            gridSystem.Columns.Add("Value", "Value");

            tabUser.Controls.Add(gridUser);
            tabSystem.Controls.Add(gridSystem);
            tabs.TabPages.Add(tabUser);
            tabs.TabPages.Add(tabSystem);

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnAdd = new Button();
            btnAdd.Text = "Add";
            btnAdd.Location = new Point(10, 8);
            btnAdd.Size = new Size(70, 32);
            btnAdd.FlatStyle = FlatStyle.Flat;
            btnAdd.BackColor = Color.FromArgb(0, 120, 80);
            btnAdd.ForeColor = Color.White;
            btnAdd.Font = btnFont;
            btnAdd.Cursor = Cursors.Hand;
            btnAdd.FlatAppearance.BorderSize = 0;

            var btnEdit = new Button();
            btnEdit.Text = "Edit";
            btnEdit.Location = new Point(90, 8);
            btnEdit.Size = new Size(70, 32);
            btnEdit.FlatStyle = FlatStyle.Flat;
            btnEdit.BackColor = Color.FromArgb(0, 80, 140);
            btnEdit.ForeColor = Color.White;
            btnEdit.Font = btnFont;
            btnEdit.Cursor = Cursors.Hand;
            btnEdit.FlatAppearance.BorderSize = 0;

            var btnDelete = new Button();
            btnDelete.Text = "Delete";
            btnDelete.Location = new Point(170, 8);
            btnDelete.Size = new Size(70, 32);
            btnDelete.FlatStyle = FlatStyle.Flat;
            btnDelete.BackColor = Color.FromArgb(180, 40, 40);
            btnDelete.ForeColor = Color.White;
            btnDelete.Font = btnFont;
            btnDelete.Cursor = Cursors.Hand;
            btnDelete.FlatAppearance.BorderSize = 0;

            var btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new Point(250, 8);
            btnRefresh.Size = new Size(80, 32);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(0, 100, 160);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = btnFont;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(560, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            var lblInfo = new Label();
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(340, 15);

            Action loadVars = () =>
            {
                gridUser.Rows.Clear();
                gridSystem.Rows.Clear();
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment", false))
                    {
                        if (k != null)
                        {
                            foreach (string name in k.GetValueNames())
                            {
                                string val = k.GetValue(name) != null ? k.GetValue(name).ToString() : "";
                                gridUser.Rows.Add(name, val);
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", false))
                    {
                        if (k != null)
                        {
                            foreach (string name in k.GetValueNames())
                            {
                                string val = k.GetValue(name) != null ? k.GetValue(name).ToString() : "";
                                gridSystem.Rows.Add(name, val);
                            }
                        }
                    }
                }
                catch { }
                lblInfo.Text = "User: " + gridUser.Rows.Count + " | System: " + gridSystem.Rows.Count;
            };

            Action<string, string, string> setRegValue = (regPath, name, val) =>
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true))
                    {
                        if (k != null) k.SetValue(name, val);
                    }
                }
                catch { }
            };

            btnAdd.Click += (s2, e2) =>
            {
                string varName = Microsoft.VisualBasic.Interaction.InputBox("Variable name:", "Add Environment Variable", "");
                if (varName.Length == 0) return;
                string varVal = Microsoft.VisualBasic.Interaction.InputBox("Variable value:", "Add Environment Variable", "");
                bool isUser = tabs.SelectedTab == tabUser;
                string regPath = isUser ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
                try
                {
                    using (var k = isUser ? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true) : Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath, true))
                    {
                        if (k != null) k.SetValue(varName, varVal);
                    }
                }
                catch { }
                loadVars();
            };

            btnEdit.Click += (s2, e2) =>
            {
                DataGridView grid = tabs.SelectedTab == tabUser ? gridUser : gridSystem;
                if (grid.SelectedRows.Count == 0) { lblInfo.Text = "Select a variable"; return; }
                string oldName = grid.SelectedRows[0].Cells["Name"].Value.ToString();
                string oldVal = grid.SelectedRows[0].Cells["Value"].Value.ToString();
                string newVal = Microsoft.VisualBasic.Interaction.InputBox("Edit value for " + oldName + ":", "Edit Variable", oldVal);
                if (newVal.Length == 0 && newVal == oldVal) return;
                bool isUser = tabs.SelectedTab == tabUser;
                string regPath = isUser ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
                try
                {
                    using (var k = isUser ? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true) : Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath, true))
                    {
                        if (k != null) k.SetValue(oldName, newVal);
                    }
                }
                catch { }
                loadVars();
            };

            btnDelete.Click += (s2, e2) =>
            {
                DataGridView grid = tabs.SelectedTab == tabUser ? gridUser : gridSystem;
                if (grid.SelectedRows.Count == 0) { lblInfo.Text = "Select a variable"; return; }
                string varName = grid.SelectedRows[0].Cells["Name"].Value.ToString();
                if (MessageBox.Show("Delete variable " + varName + "?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                bool isUser = tabs.SelectedTab == tabUser;
                string regPath = isUser ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
                try
                {
                    using (var k = isUser ? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true) : Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath, true))
                    {
                        if (k != null) k.DeleteValue(varName, false);
                    }
                }
                catch { }
                loadVars();
            };

            btnRefresh.Click += (s2, e2) => loadVars();
            loadVars();

            panel.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDelete, btnRefresh, btnClose, lblInfo });
            f.Controls.Add(tabs);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { tabs.Font.Dispose(); gridUser.Font.Dispose(); gridSystem.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Environment Variables closed");
        }

        void OpenHostsEditor()
        {
            var f = new Form();
            f.Text = "Hosts Editor";
            f.Size = new Size(600, 500);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var txtHosts = new TextBox();
            txtHosts.Multiline = true;
            txtHosts.ScrollBars = ScrollBars.Both;
            txtHosts.Font = new Font("Consolas", 10);
            txtHosts.Dock = DockStyle.Fill;
            txtHosts.BackColor = Color.FromArgb(20, 20, 35);
            txtHosts.ForeColor = Color.Lime;
            txtHosts.BorderStyle = BorderStyle.None;
            txtHosts.WordWrap = false;

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnSave = new Button();
            btnSave.Text = "Save";
            btnSave.Location = new Point(10, 8);
            btnSave.Size = new Size(80, 32);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.BackColor = Color.FromArgb(0, 120, 80);
            btnSave.ForeColor = Color.White;
            btnSave.Font = btnFont;
            btnSave.Cursor = Cursors.Hand;
            btnSave.FlatAppearance.BorderSize = 0;

            var btnReset = new Button();
            btnReset.Text = "Reset";
            btnReset.Location = new Point(100, 8);
            btnReset.Size = new Size(80, 32);
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.BackColor = Color.FromArgb(200, 120, 0);
            btnReset.ForeColor = Color.White;
            btnReset.Font = btnFont;
            btnReset.Cursor = Cursors.Hand;
            btnReset.FlatAppearance.BorderSize = 0;

            var btnApply = new Button();
            btnApply.Text = "Apply";
            btnApply.Location = new Point(190, 8);
            btnApply.Size = new Size(80, 32);
            btnApply.FlatStyle = FlatStyle.Flat;
            btnApply.BackColor = Color.FromArgb(0, 80, 140);
            btnApply.ForeColor = Color.White;
            btnApply.Font = btnFont;
            btnApply.Cursor = Cursors.Hand;
            btnApply.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(510, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            var lblInfo = new Label();
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(280, 15);

            string hostsPath2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

            Action loadHosts = () =>
            {
                try
                {
                    if (File.Exists(hostsPath2)) txtHosts.Text = File.ReadAllText(hostsPath2);
                    else txtHosts.Text = "# Hosts file not found";
                    lblInfo.Text = "Loaded";
                }
                catch { lblInfo.Text = "Error loading hosts file"; }
            };

            btnSave.Click += (s2, e2) =>
            {
                try
                {
                    File.WriteAllText(hostsPath2, txtHosts.Text);
                    lblInfo.Text = "Saved";
                }
                catch { lblInfo.Text = "Error saving (run as admin)"; }
            };

            btnReset.Click += (s2, e2) => loadHosts();

            btnApply.Click += (s2, e2) =>
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                    if (p != null) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
                    lblInfo.Text = "DNS flushed";
                }
                catch { lblInfo.Text = "Error flushing DNS"; }
            };

            loadHosts();

            panel.Controls.AddRange(new Control[] { btnSave, btnReset, btnApply, btnClose, lblInfo });
            f.Controls.Add(txtHosts);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { txtHosts.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Hosts Editor closed");
        }

        void OpenPowerPlans()
        {
            var f = new Form();
            f.Text = "Power Plans";
            f.Size = new Size(550, 420);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var list = new ListBox();
            list.Font = new Font("Consolas", 10);
            list.BackColor = Color.FromArgb(20, 20, 35);
            list.ForeColor = Color.FromArgb(0, 200, 100);
            list.Dock = DockStyle.Fill;
            list.SelectionMode = SelectionMode.One;

            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 80;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnSetActive = new Button();
            btnSetActive.Text = "Set Active";
            btnSetActive.Location = new Point(10, 8);
            btnSetActive.Size = new Size(90, 32);
            btnSetActive.FlatStyle = FlatStyle.Flat;
            btnSetActive.BackColor = Color.FromArgb(0, 120, 80);
            btnSetActive.ForeColor = Color.White;
            btnSetActive.Font = btnFont;
            btnSetActive.Cursor = Cursors.Hand;
            btnSetActive.FlatAppearance.BorderSize = 0;

            var btnCreateBalanced = new Button();
            btnCreateBalanced.Text = "Create Balanced";
            btnCreateBalanced.Location = new Point(110, 8);
            btnCreateBalanced.Size = new Size(120, 32);
            btnCreateBalanced.FlatStyle = FlatStyle.Flat;
            btnCreateBalanced.BackColor = Color.FromArgb(0, 80, 140);
            btnCreateBalanced.ForeColor = Color.White;
            btnCreateBalanced.Font = btnFont;
            btnCreateBalanced.Cursor = Cursors.Hand;
            btnCreateBalanced.FlatAppearance.BorderSize = 0;

            var btnCreateHigh = new Button();
            btnCreateHigh.Text = "Create High Perf";
            btnCreateHigh.Location = new Point(240, 8);
            btnCreateHigh.Size = new Size(120, 32);
            btnCreateHigh.FlatStyle = FlatStyle.Flat;
            btnCreateHigh.BackColor = Color.FromArgb(200, 120, 0);
            btnCreateHigh.ForeColor = Color.White;
            btnCreateHigh.Font = btnFont;
            btnCreateHigh.Cursor = Cursors.Hand;
            btnCreateHigh.FlatAppearance.BorderSize = 0;

            var btnCreatePowerSaver = new Button();
            btnCreatePowerSaver.Text = "Create Power Saver";
            btnCreatePowerSaver.Location = new Point(10, 48);
            btnCreatePowerSaver.Size = new Size(140, 32);
            btnCreatePowerSaver.FlatStyle = FlatStyle.Flat;
            btnCreatePowerSaver.BackColor = Color.FromArgb(100, 60, 0);
            btnCreatePowerSaver.ForeColor = Color.White;
            btnCreatePowerSaver.Font = btnFont;
            btnCreatePowerSaver.Cursor = Cursors.Hand;
            btnCreatePowerSaver.FlatAppearance.BorderSize = 0;

            var btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new Point(370, 8);
            btnRefresh.Size = new Size(80, 32);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(0, 100, 160);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = btnFont;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(460, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            Action loadPlans = () =>
            {
                list.Items.Clear();
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "/list");
                    psi.RedirectStandardOutput = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        p.Dispose();
                        string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            list.Items.Add(line.TrimEnd('\r'));
                        }
                    }
                }
                catch { list.Items.Add("Error loading power plans"); }
            };

            btnSetActive.Click += (s2, e2) =>
            {
                if (list.SelectedIndex < 0) { MessageBox.Show("Select a power plan first.", "Power Plans"); return; }
                string selected = list.SelectedItem.ToString();
                int guidStart = selected.IndexOf("(");
                int guidEnd = selected.IndexOf(")");
                if (guidStart < 0 || guidEnd < 0) { MessageBox.Show("Could not parse GUID.", "Power Plans"); return; }
                string guid = selected.Substring(guidStart + 1, guidEnd - guidStart - 1);
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "/setactive " + guid);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                    loadPlans();
                }
                catch { MessageBox.Show("Failed to set active plan.", "Power Plans"); }
            };

            btnCreateBalanced.Click += (s2, e2) =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "-duplicatescheme 381b4222-f694-41f0-9685-ff5bb260df2e");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    var p = Process.Start(psi);
                    if (p != null) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
                    loadPlans();
                }
                catch { }
            };

            btnCreateHigh.Click += (s2, e2) =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    var p = Process.Start(psi);
                    if (p != null) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
                    loadPlans();
                }
                catch { }
            };

            btnCreatePowerSaver.Click += (s2, e2) =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "-duplicatescheme a1841308-3541-4fab-bc81-f71556f20b4a");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    var p = Process.Start(psi);
                    if (p != null) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
                    loadPlans();
                }
                catch { }
            };

            btnRefresh.Click += (s2, e2) => loadPlans();
            loadPlans();

            panel.Controls.AddRange(new Control[] { btnSetActive, btnCreateBalanced, btnCreateHigh, btnCreatePowerSaver, btnRefresh, btnClose });
            f.Controls.Add(list);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { list.Font.Dispose(); btnFont.Dispose(); ico.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Power Plans closed");
        }

        void OpenScheduledTasks()
        {
            var f = new Form();
            f.Text = "Scheduled Tasks";
            f.Size = new Size(750, 520);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            var grid = new DataGridView();
            grid.Font = new Font("Consolas", 9);
            grid.BackColor = Color.FromArgb(20, 20, 35);
            grid.ForeColor = Color.White;
            grid.GridColor = Color.FromArgb(40, 40, 60);
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            grid.Dock = DockStyle.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.FromArgb(20, 20, 35);

            grid.Columns.Add("TaskName", "Task Name");
            grid.Columns.Add("Status", "Status");
            grid.Columns.Add("NextRun", "Next Run");
            grid.Columns.Add("LastRun", "Last Run");

            var ctx = new ContextMenuStrip();
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new Point(10, 8);
            btnRefresh.Size = new Size(80, 32);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(0, 100, 160);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = btnFont;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(660, 8);
            btnClose.Size = new Size(70, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            var lblInfo = new Label();
            lblInfo.Text = "";
            lblInfo.Font = new Font("Segoe UI", 8);
            lblInfo.ForeColor = Color.FromArgb(80, 80, 100);
            lblInfo.AutoSize = true;
            lblInfo.Location = new Point(100, 15);

            ctx.Items.Add("Run Now", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string taskName = grid.SelectedRows[0].Cells["TaskName"].Value.ToString();
                taskName = taskName.Replace("\"", "\"\"");
                try
                {
                    var psi = new ProcessStartInfo("schtasks", "/run /tn \"" + taskName + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                    lblInfo.Text = "Ran: " + taskName;
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            ctx.Items.Add("End", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string taskName = grid.SelectedRows[0].Cells["TaskName"].Value.ToString();
                taskName = taskName.Replace("\"", "\"\"");
                try
                {
                    var psi = new ProcessStartInfo("schtasks", "/end /tn \"" + taskName + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                    lblInfo.Text = "Ended: " + taskName;
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            ctx.Items.Add("Delete", null, (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) return;
                string taskName = grid.SelectedRows[0].Cells["TaskName"].Value.ToString();
                taskName = taskName.Replace("\"", "\"\"");
                if (MessageBox.Show("Delete task " + taskName + "?", "Delete Task", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    var psi = new ProcessStartInfo("schtasks", "/delete /tn \"" + taskName + "\" /f");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                    lblInfo.Text = "Deleted: " + taskName;
                    btnRefresh.PerformClick();
                }
                catch (Exception ex) { lblInfo.Text = "Error: " + ex.Message; }
            });
            grid.ContextMenuStrip = ctx;

            Action loadTasks = () =>
            {
                grid.Rows.Clear();
                try
                {
                    var psi = new ProcessStartInfo("schtasks", "/query /fo CSV /v");
                    psi.RedirectStandardOutput = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        p.Dispose();
                        string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        bool headerSkipped = false;
                        foreach (string line in lines)
                        {
                            if (!headerSkipped) { headerSkipped = true; continue; }
                            string[] parts = ParseCsvLine(line.TrimEnd('\r'));
                            if (parts.Length >= 4)
                            {
                                string taskName = parts[0].Trim('"');
                                string status = parts.Length > 1 ? parts[1].Trim('"') : "";
                                string nextRun = parts.Length > 2 ? parts[2].Trim('"') : "";
                                string lastRun = parts.Length > 3 ? parts[3].Trim('"') : "";
                                if (taskName.Length > 0)
                                    grid.Rows.Add(taskName, status, nextRun, lastRun);
                            }
                        }
                    }
                }
                catch { }
                lblInfo.Text = grid.Rows.Count + " tasks found";
            };

            btnRefresh.Click += (s2, e2) => loadTasks();
            loadTasks();

            panel.Controls.AddRange(new Control[] { btnRefresh, btnClose, lblInfo });
            f.Controls.Add(grid);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { grid.Font.Dispose(); btnFont.Dispose(); lblInfo.Font.Dispose(); ico.Dispose(); ctx.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Scheduled Tasks closed");
        }

        void OpenDiskHealth()
        {
            var f = new Form();
            f.Text = "Disk Health";
            f.Size = new Size(780, 520);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font gridFont = new Font("Consolas", 9);

            var lblPhysical = new Label();
            lblPhysical.Text = "Physical Disks";
            lblPhysical.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblPhysical.ForeColor = Color.FromArgb(0, 170, 255);
            lblPhysical.AutoSize = true;
            lblPhysical.Location = new Point(10, 5);

            var gridPhysical = new DataGridView();
            gridPhysical.Font = gridFont;
            gridPhysical.BackColor = Color.FromArgb(20, 20, 35);
            gridPhysical.ForeColor = Color.White;
            gridPhysical.GridColor = Color.FromArgb(40, 40, 60);
            gridPhysical.BorderStyle = BorderStyle.None;
            gridPhysical.AllowUserToAddRows = false;
            gridPhysical.ReadOnly = true;
            gridPhysical.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            gridPhysical.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridPhysical.MultiSelect = false;
            gridPhysical.Location = new Point(10, 28);
            gridPhysical.Size = new Size(745, 180);
            gridPhysical.BackgroundColor = Color.FromArgb(20, 20, 35);

            gridPhysical.Columns.Add("Model", "Model");
            gridPhysical.Columns.Add("Size", "Size");
            gridPhysical.Columns.Add("Status", "Status");
            gridPhysical.Columns.Add("MediaType", "Media Type");

            var lblLogical = new Label();
            lblLogical.Text = "Logical Disks";
            lblLogical.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblLogical.ForeColor = Color.FromArgb(0, 170, 255);
            lblLogical.AutoSize = true;
            lblLogical.Location = new Point(10, 220);

            var gridLogical = new DataGridView();
            gridLogical.Font = gridFont;
            gridLogical.BackColor = Color.FromArgb(20, 20, 35);
            gridLogical.ForeColor = Color.White;
            gridLogical.GridColor = Color.FromArgb(40, 40, 60);
            gridLogical.BorderStyle = BorderStyle.None;
            gridLogical.AllowUserToAddRows = false;
            gridLogical.ReadOnly = true;
            gridLogical.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            gridLogical.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridLogical.MultiSelect = false;
            gridLogical.Location = new Point(10, 243);
            gridLogical.Size = new Size(745, 180);
            gridLogical.BackgroundColor = Color.FromArgb(20, 20, 35);

            gridLogical.Columns.Add("Drive", "Drive");
            gridLogical.Columns.Add("FileSystem", "File System");
            gridLogical.Columns.Add("TotalSize", "Total Size");
            gridLogical.Columns.Add("FreeSpace", "Free Space");
            gridLogical.Columns.Add("PercentUsed", "% Used");

            var barUsed = new ProgressBar();
            barUsed.Location = new Point(10, 432);
            barUsed.Size = new Size(500, 20);
            barUsed.Style = ProgressBarStyle.Continuous;

            var lblUsedInfo = new Label();
            lblUsedInfo.Text = "";
            lblUsedInfo.Font = lblFont;
            lblUsedInfo.ForeColor = Color.FromArgb(120, 120, 140);
            lblUsedInfo.AutoSize = true;
            lblUsedInfo.Location = new Point(520, 434);

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(680, 458);
            btnClose.Size = new Size(75, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            Action loadData = () =>
            {
                gridPhysical.Rows.Clear();
                gridLogical.Rows.Clear();
                try
                {
                    var psi1 = new ProcessStartInfo("wmic", "diskdrive get Model,Size,Status,MediaType /format:csv");
                    psi1.RedirectStandardOutput = true;
                    psi1.UseShellExecute = false;
                    psi1.CreateNoWindow = true;
                    var p1 = Process.Start(psi1);
                    if (p1 != null)
                    {
                        string output1 = p1.StandardOutput.ReadToEnd();
                        p1.WaitForExit();
                        p1.Dispose();
                        string[] lines1 = output1.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < lines1.Length; i++)
                        {
                            string line = lines1[i].TrimEnd('\r');
                            string[] parts = line.Split(',');
                            if (parts.Length >= 5)
                            {
                                string model = parts[2].Trim();
                                string size = parts[4].Trim();
                                string status = parts[3].Trim();
                                string mediaType = parts[1].Trim();
                                string sizeGB = "";
                                long sizeBytes;
                                if (long.TryParse(size, out sizeBytes))
                                    sizeGB = (sizeBytes / 1073741824.0).ToString("F1") + " GB";
                                else
                                    sizeGB = size;
                                int rowIdx = gridPhysical.Rows.Add(model, sizeGB, status, mediaType);
                                DataGridViewRow row = gridPhysical.Rows[rowIdx];
                                string statusLower = status.ToLower();
                                if (statusLower.Contains("ok") || statusLower.Contains("good"))
                                    row.Cells[2].Style.ForeColor = Color.Lime;
                                else if (statusLower.Contains("caution") || statusLower.Contains("warn"))
                                    row.Cells[2].Style.ForeColor = Color.Yellow;
                                else if (statusLower.Contains("bad") || statusLower.Contains("fail") || statusLower.Contains("error"))
                                    row.Cells[2].Style.ForeColor = Color.Red;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    var psi2 = new ProcessStartInfo("wmic", "logicaldisk get Size,FreeSpace,FileSystem,DeviceID /format:csv");
                    psi2.RedirectStandardOutput = true;
                    psi2.UseShellExecute = false;
                    psi2.CreateNoWindow = true;
                    var p2 = Process.Start(psi2);
                    if (p2 != null)
                    {
                        string output2 = p2.StandardOutput.ReadToEnd();
                        p2.WaitForExit();
                        p2.Dispose();
                        string[] lines2 = output2.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < lines2.Length; i++)
                        {
                            string line = lines2[i].TrimEnd('\r');
                            string[] parts = line.Split(',');
                            if (parts.Length >= 5)
                            {
                                string driveId = parts[1].Trim();
                                string fileSystem = parts[2].Trim();
                                string freeSpace = parts[3].Trim();
                                string totalSize = parts[4].Trim();
                                long totalBytes, freeBytes;
                                string totalStr = "", freeStr = "";
                                if (long.TryParse(totalSize, out totalBytes))
                                    totalStr = (totalBytes / 1073741824.0).ToString("F1") + " GB";
                                else
                                    totalStr = totalSize;
                                if (long.TryParse(freeSpace, out freeBytes))
                                    freeStr = (freeBytes / 1073741824.0).ToString("F1") + " GB";
                                else
                                    freeStr = freeSpace;
                                double pctUsed = 0;
                                if (totalBytes > 0)
                                    pctUsed = ((totalBytes - (long.TryParse(freeSpace, out freeBytes) ? freeBytes : 0)) * 100.0 / totalBytes);
                                string pctStr = pctUsed.ToString("F1") + "%";
                                gridLogical.Rows.Add(driveId, fileSystem, totalStr, freeStr, pctStr);
                            }
                        }
                    }
                }
                catch { }
            };

            loadData();
            btnClose.Click += (s2, e2) => f.Close();

            f.Controls.AddRange(new Control[] { lblPhysical, gridPhysical, lblLogical, gridLogical, barUsed, lblUsedInfo, btnClose });
            f.ShowDialog(this);
            lblPhysical.Font.Dispose();
            lblLogical.Font.Dispose();
            gridPhysical.Font.Dispose();
            gridLogical.Font.Dispose();
            lblFont.Dispose();
            btnFont.Dispose();
            gridFont.Dispose();
            ico.Dispose();
            SetStatus("Disk Health closed");
        }

        void OpenGpuMonitor()
        {
            var f = new Form();
            f.Text = "GPU Monitor";
            f.Size = new Size(450, 300);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 11, FontStyle.Bold);

            var lblGpuName = new Label();
            lblGpuName.Text = "GPU: Detecting...";
            lblGpuName.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblGpuName.ForeColor = Color.FromArgb(0, 170, 255);
            lblGpuName.AutoSize = true;
            lblGpuName.Location = new Point(10, 10);

            var lblVram = new Label();
            lblVram.Text = "VRAM: -";
            lblVram.Font = lblFont;
            lblVram.ForeColor = Color.White;
            lblVram.AutoSize = true;
            lblVram.Location = new Point(10, 38);

            var lblDriver = new Label();
            lblDriver.Text = "Driver: -";
            lblDriver.Font = lblFont;
            lblDriver.ForeColor = Color.White;
            lblDriver.AutoSize = true;
            lblDriver.Location = new Point(10, 62);

            var lblUsage = new Label();
            lblUsage.Text = "GPU Usage: -%";
            lblUsage.Font = monoFont;
            lblUsage.ForeColor = Color.Lime;
            lblUsage.AutoSize = true;
            lblUsage.Location = new Point(10, 95);

            var lblMemInfo = new Label();
            lblMemInfo.Text = "VRAM Used: -";
            lblMemInfo.Font = lblFont;
            lblMemInfo.ForeColor = Color.Cyan;
            lblMemInfo.AutoSize = true;
            lblMemInfo.Location = new Point(10, 125);

            var barGpu = new ProgressBar();
            barGpu.Location = new Point(10, 155);
            barGpu.Size = new Size(410, 22);
            barGpu.Style = ProgressBarStyle.Continuous;

            var lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.Font = lblFont;
            lblStatus.ForeColor = Color.FromArgb(100, 100, 120);
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(10, 185);

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(350, 220);
            btnClose.Size = new Size(75, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            try
            {
                var psi = new ProcessStartInfo("wmic", "path win32_videocontroller get Name,AdapterRAM,DriverVersion /format:csv");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    p.Dispose();
                    string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i].TrimEnd('\r');
                        string[] parts = line.Split(',');
                        if (parts.Length >= 4)
                        {
                            string name = parts[2].Trim();
                            string adapterRam = parts[1].Trim();
                            string driver = parts[3].Trim();
                            long ramBytes;
                            string ramStr = "";
                            if (long.TryParse(adapterRam, out ramBytes) && ramBytes > 0)
                                ramStr = (ramBytes / 1073741824.0).ToString("F1") + " GB";
                            else
                                ramStr = adapterRam;
                            lblGpuName.Text = "GPU: " + name;
                            lblVram.Text = "VRAM: " + ramStr;
                            lblDriver.Text = "Driver: " + driver;
                            break;
                        }
                    }
                }
            }
            catch { }

            bool nvidiaAvailable = true;

            var ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 1000;
            ticker.Tick += (s, e) =>
            {
                try
                {
                    if (nvidiaAvailable)
                    {
                        var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader");
                        psi.RedirectStandardOutput = true;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        var p = Process.Start(psi);
                        if (p != null)
                        {
                            string output = p.StandardOutput.ReadToEnd();
                            p.WaitForExit();
                            p.Dispose();
                            string[] parts = output.Trim().Split(',');
                            if (parts.Length >= 3)
                            {
                                string gpuPct = parts[0].Trim().Replace("%", "").Trim();
                                string memUsed = parts[1].Trim();
                                string memTotal = parts[2].Trim();
                                int pctVal;
                                if (int.TryParse(gpuPct, out pctVal))
                                {
                                    barGpu.Value = Math.Min(pctVal, 100);
                                    lblUsage.Text = "GPU Usage: " + pctVal + "%";
                                    lblUsage.ForeColor = pctVal < 50 ? Color.Lime : pctVal < 80 ? Color.Yellow : Color.Red;
                                }
                                lblMemInfo.Text = "VRAM Used: " + memUsed + " / " + memTotal;
                                lblStatus.Text = "Live monitoring (nvidia-smi)";
                                return;
                            }
                        }
                    }
                }
                catch { nvidiaAvailable = false; }

                try
                {
                    var psi = new ProcessStartInfo("wmic", "cpu get LoadPercentage /format:csv");
                    psi.RedirectStandardOutput = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        p.Dispose();
                        string[] lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            string line = lines[i].TrimEnd('\r');
                            string[] parts = line.Split(',');
                            if (parts.Length >= 2)
                            {
                                string loadStr = parts[1].Trim();
                                int loadVal;
                                if (int.TryParse(loadStr, out loadVal))
                                {
                                    barGpu.Value = Math.Min(loadVal, 100);
                                    lblUsage.Text = "GPU Usage (CPU fallback): " + loadVal + "%";
                                    lblUsage.ForeColor = loadVal < 50 ? Color.Lime : loadVal < 80 ? Color.Yellow : Color.Red;
                                }
                                break;
                            }
                        }
                        lblMemInfo.Text = "VRAM: nvidia-smi not available";
                        lblStatus.Text = "Fallback to CPU load";
                    }
                }
                catch { }
            };
            ticker.Start();

            f.Controls.AddRange(new Control[] { lblGpuName, lblVram, lblDriver, lblUsage, lblMemInfo, barGpu, lblStatus, btnClose });
            f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); lblFont.Dispose(); btnFont.Dispose(); monoFont.Dispose(); ico.Dispose(); lblGpuName.Font.Dispose(); };
            f.ShowDialog(this);
            SetStatus("GPU Monitor closed");
        }

        void OpenBatteryReport()
        {
            var f = new Form();
            f.Text = "Battery Report";
            f.Size = new Size(480, 420);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font bigFont = new Font("Consolas", 20, FontStyle.Bold);

            var lblTitle = new Label();
            lblTitle.Text = "Battery Information";
            lblTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 170, 255);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(10, 10);

            var lblDesignCap = new Label();
            lblDesignCap.Text = "Design Capacity: -";
            lblDesignCap.Font = lblFont;
            lblDesignCap.ForeColor = Color.White;
            lblDesignCap.AutoSize = true;
            lblDesignCap.Location = new Point(10, 45);

            var lblFullCharge = new Label();
            lblFullCharge.Text = "Full Charge Capacity: -";
            lblFullCharge.Font = lblFont;
            lblFullCharge.ForeColor = Color.White;
            lblFullCharge.AutoSize = true;
            lblFullCharge.Location = new Point(10, 70);

            var lblCycleCount = new Label();
            lblCycleCount.Text = "Cycle Count: -";
            lblCycleCount.Font = lblFont;
            lblCycleCount.ForeColor = Color.White;
            lblCycleCount.AutoSize = true;
            lblCycleCount.Location = new Point(10, 95);

            var lblChargeRate = new Label();
            lblChargeRate.Text = "Charge Rate: -";
            lblChargeRate.Font = lblFont;
            lblChargeRate.ForeColor = Color.White;
            lblChargeRate.AutoSize = true;
            lblChargeRate.Location = new Point(10, 120);

            var lblTimeRemaining = new Label();
            lblTimeRemaining.Text = "Estimated Time Remaining: -";
            lblTimeRemaining.Font = lblFont;
            lblTimeRemaining.ForeColor = Color.White;
            lblTimeRemaining.AutoSize = true;
            lblTimeRemaining.Location = new Point(10, 145);

            var lblHealthPct = new Label();
            lblHealthPct.Text = "Battery Health: -";
            lblHealthPct.Font = bigFont;
            lblHealthPct.ForeColor = Color.Lime;
            lblHealthPct.AutoSize = true;
            lblHealthPct.Location = new Point(10, 180);

            var barHealth = new ProgressBar();
            barHealth.Location = new Point(10, 225);
            barHealth.Size = new Size(440, 30);
            barHealth.Style = ProgressBarStyle.Continuous;

            var lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.Font = lblFont;
            lblStatus.ForeColor = Color.FromArgb(100, 100, 120);
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(10, 265);

            var btnOpenReport = new Button();
            btnOpenReport.Text = "Open Report";
            btnOpenReport.Location = new Point(10, 300);
            btnOpenReport.Size = new Size(110, 32);
            btnOpenReport.FlatStyle = FlatStyle.Flat;
            btnOpenReport.BackColor = Color.FromArgb(0, 100, 180);
            btnOpenReport.ForeColor = Color.White;
            btnOpenReport.Font = btnFont;
            btnOpenReport.Cursor = Cursors.Hand;
            btnOpenReport.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(380, 345);
            btnClose.Size = new Size(75, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            string reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "battery.html");

            btnOpenReport.Click += (s2, e2) =>
            {
                try
                {
                    if (File.Exists(reportPath))
                        Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
                    else
                        lblStatus.Text = "Report not found. Click Generate first.";
                }
                catch { lblStatus.Text = "Could not open report"; }
            };

            try
            {
                var psi = new ProcessStartInfo("powercfg", "/batteryreport /output " + reportPath + " /xml");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                var p = Process.Start(psi);
                if (p != null) { p.WaitForExit(); p.Dispose(); }

                if (File.Exists(reportPath))
                {
                    string xmlContent = File.ReadAllText(reportPath);
                    int designCapIdx = xmlContent.IndexOf("DesignCapacity");
                    int fullCapIdx = xmlContent.IndexOf("FullChargeCapacity");
                    int cycleIdx = xmlContent.IndexOf("CycleCount");

                    if (designCapIdx > 0)
                    {
                        int start = xmlContent.IndexOf(">", designCapIdx) + 1;
                        int end = xmlContent.IndexOf("<", start);
                        if (end > start)
                        {
                            string val = xmlContent.Substring(start, end - start).Trim();
                            long designMwh;
                            if (long.TryParse(val, out designMwh))
                                lblDesignCap.Text = "Design Capacity: " + designMwh + " mWh";
                            else
                                lblDesignCap.Text = "Design Capacity: " + val;
                        }
                    }

                    if (fullCapIdx > 0)
                    {
                        int start = xmlContent.IndexOf(">", fullCapIdx) + 1;
                        int end = xmlContent.IndexOf("<", start);
                        if (end > start)
                        {
                            string val = xmlContent.Substring(start, end - start).Trim();
                            long fullMwh;
                            if (long.TryParse(val, out fullMwh))
                                lblFullCharge.Text = "Full Charge Capacity: " + fullMwh + " mWh";
                            else
                                lblFullCharge.Text = "Full Charge Capacity: " + val;

                            long designMwh2;
                            if (long.TryParse(val, out fullMwh) && designCapIdx > 0)
                            {
                                int dStart = xmlContent.IndexOf(">", designCapIdx) + 1;
                                int dEnd = xmlContent.IndexOf("<", dStart);
                                if (dEnd > dStart && long.TryParse(xmlContent.Substring(dStart, dEnd - dStart).Trim(), out designMwh2) && designMwh2 > 0)
                                {
                                    double health = (fullMwh * 100.0 / designMwh2);
                                    int healthInt = (int)health;
                                    barHealth.Value = Math.Min(healthInt, 100);
                                    lblHealthPct.Text = "Battery Health: " + healthInt + "%";
                                    if (healthInt > 80) lblHealthPct.ForeColor = Color.Lime;
                                    else if (healthInt > 50) lblHealthPct.ForeColor = Color.Yellow;
                                    else lblHealthPct.ForeColor = Color.Red;
                                }
                            }
                        }
                    }

                    if (cycleIdx > 0)
                    {
                        int start = xmlContent.IndexOf(">", cycleIdx) + 1;
                        int end = xmlContent.IndexOf("<", start);
                        if (end > start)
                            lblCycleCount.Text = "Cycle Count: " + xmlContent.Substring(start, end - start).Trim();
                    }

                    lblStatus.Text = "Battery report generated";
                }
            }
            catch { lblStatus.Text = "Could not generate battery report"; }

            try
            {
                var psi2 = new ProcessStartInfo("powercfg", "/query SCHEME_CURRENT SUB_BATTERY BATTERY_STATISTICS");
                psi2.RedirectStandardOutput = true;
                psi2.UseShellExecute = false;
                psi2.CreateNoWindow = true;
                var p2 = Process.Start(psi2);
                if (p2 != null)
                {
                    string output = p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit();
                    p2.Dispose();
                    if (output.Length > 0)
                        lblChargeRate.Text = "Charge Info: Available (see powercfg)";
                }
            }
            catch { }

            f.Controls.AddRange(new Control[] { lblTitle, lblDesignCap, lblFullCharge, lblCycleCount, lblChargeRate, lblTimeRemaining, lblHealthPct, barHealth, lblStatus, btnOpenReport, btnClose });
            f.ShowDialog(this);
            lblTitle.Font.Dispose();
            lblFont.Dispose();
            btnFont.Dispose();
            bigFont.Dispose();
            ico.Dispose();
            SetStatus("Battery Report closed");
        }

        void OpenSystemInfoPro()
        {
            var f = new Form();
            f.Text = "System Info Pro";
            f.Size = new Size(600, 500);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 9);

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;

            var tabCpu = new TabPage("CPU");
            tabCpu.BackColor = Color.FromArgb(20, 20, 35);

            var lblCpuName = new Label();
            lblCpuName.Text = "CPU: -";
            lblCpuName.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblCpuName.ForeColor = Color.Lime;
            lblCpuName.AutoSize = true;
            lblCpuName.Location = new Point(10, 10);
            tabCpu.Controls.Add(lblCpuName);

            var lblCpuCores = new Label();
            lblCpuCores.Text = "Cores: -";
            lblCpuCores.Font = lblFont;
            lblCpuCores.ForeColor = Color.White;
            lblCpuCores.AutoSize = true;
            lblCpuCores.Location = new Point(10, 38);
            tabCpu.Controls.Add(lblCpuCores);

            var lblCpuUsage = new Label();
            lblCpuUsage.Text = "Usage: -%";
            lblCpuUsage.Font = new Font("Consolas", 14, FontStyle.Bold);
            lblCpuUsage.ForeColor = Color.Lime;
            lblCpuUsage.AutoSize = true;
            lblCpuUsage.Location = new Point(10, 65);
            tabCpu.Controls.Add(lblCpuUsage);

            var barCpu = new ProgressBar();
            barCpu.Location = new Point(10, 95);
            barCpu.Size = new Size(540, 20);
            barCpu.Style = ProgressBarStyle.Continuous;
            tabCpu.Controls.Add(barCpu);

            var tabMemory = new TabPage("Memory");
            tabMemory.BackColor = Color.FromArgb(20, 20, 35);

            var lblRamTotal = new Label();
            lblRamTotal.Text = "Total RAM: -";
            lblRamTotal.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblRamTotal.ForeColor = Color.Cyan;
            lblRamTotal.AutoSize = true;
            lblRamTotal.Location = new Point(10, 10);
            tabMemory.Controls.Add(lblRamTotal);

            var lblRamUsed = new Label();
            lblRamUsed.Text = "Used: -";
            lblRamUsed.Font = lblFont;
            lblRamUsed.ForeColor = Color.White;
            lblRamUsed.AutoSize = true;
            lblRamUsed.Location = new Point(10, 38);
            tabMemory.Controls.Add(lblRamUsed);

            var lblRamAvail = new Label();
            lblRamAvail.Text = "Available: -";
            lblRamAvail.Font = lblFont;
            lblRamAvail.ForeColor = Color.White;
            lblRamAvail.AutoSize = true;
            lblRamAvail.Location = new Point(10, 62);
            tabMemory.Controls.Add(lblRamAvail);

            var barRam = new ProgressBar();
            barRam.Location = new Point(10, 95);
            barRam.Size = new Size(540, 20);
            barRam.Style = ProgressBarStyle.Continuous;
            tabMemory.Controls.Add(barRam);

            var tabSystem = new TabPage("System");
            tabSystem.BackColor = Color.FromArgb(20, 20, 35);

            var lblOsName = new Label();
            lblOsName.Text = "OS: -";
            lblOsName.Font = lblFont;
            lblOsName.ForeColor = Color.White;
            lblOsName.AutoSize = true;
            lblOsName.Location = new Point(10, 10);
            tabSystem.Controls.Add(lblOsName);

            var lblOsVer = new Label();
            lblOsVer.Text = "Version: -";
            lblOsVer.Font = lblFont;
            lblOsVer.ForeColor = Color.White;
            lblOsVer.AutoSize = true;
            lblOsVer.Location = new Point(10, 35);
            tabSystem.Controls.Add(lblOsVer);

            var lblUptime = new Label();
            lblUptime.Text = "Uptime: -";
            lblUptime.Font = lblFont;
            lblUptime.ForeColor = Color.White;
            lblUptime.AutoSize = true;
            lblUptime.Location = new Point(10, 60);
            tabSystem.Controls.Add(lblUptime);

            var txtDiskInfo = new TextBox();
            txtDiskInfo.Multiline = true;
            txtDiskInfo.ReadOnly = true;
            txtDiskInfo.Font = monoFont;
            txtDiskInfo.BackColor = Color.FromArgb(25, 25, 35);
            txtDiskInfo.ForeColor = Color.Lime;
            txtDiskInfo.BorderStyle = BorderStyle.None;
            txtDiskInfo.Location = new Point(10, 90);
            txtDiskInfo.Size = new Size(540, 180);
            txtDiskInfo.ScrollBars = ScrollBars.Vertical;
            tabSystem.Controls.Add(txtDiskInfo);

            var tabNetwork = new TabPage("Network");
            tabNetwork.BackColor = Color.FromArgb(20, 20, 35);

            var txtNetInfo = new TextBox();
            txtNetInfo.Multiline = true;
            txtNetInfo.ReadOnly = true;
            txtNetInfo.Font = monoFont;
            txtNetInfo.BackColor = Color.FromArgb(25, 25, 35);
            txtNetInfo.ForeColor = Color.Cyan;
            txtNetInfo.BorderStyle = BorderStyle.None;
            txtNetInfo.Dock = DockStyle.Fill;
            txtNetInfo.ScrollBars = ScrollBars.Vertical;
            tabNetwork.Controls.Add(txtNetInfo);

            tabs.TabPages.Add(tabCpu);
            tabs.TabPages.Add(tabMemory);
            tabs.TabPages.Add(tabSystem);
            tabs.TabPages.Add(tabNetwork);

            var lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.Font = lblFont;
            lblStatus.ForeColor = Color.FromArgb(80, 80, 100);
            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 22;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Dock = DockStyle.Bottom;
            btnClose.Height = 32;
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            System.Diagnostics.PerformanceCounter perfCpu = null;
            try { perfCpu = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); perfCpu.NextValue(); }
            catch { }

            Action refreshInfo = () =>
            {
                try
                {
                    lblCpuCores.Text = "Cores: " + Environment.ProcessorCount;

                    if (perfCpu != null)
                    {
                        try
                        {
                            float cpuUsage = perfCpu.NextValue();
                            barCpu.Value = Math.Min((int)cpuUsage, 100);
                            lblCpuUsage.Text = "Usage: " + cpuUsage.ToString("F1") + "%";
                            lblCpuUsage.ForeColor = cpuUsage < 50 ? Color.Lime : cpuUsage < 80 ? Color.Yellow : Color.Red;
                        }
                        catch { }
                    }

                    try
                    {
                        var psiCpu = new ProcessStartInfo("wmic", "cpu get Name /format:csv");
                        psiCpu.RedirectStandardOutput = true;
                        psiCpu.UseShellExecute = false;
                        psiCpu.CreateNoWindow = true;
                        var pCpu = Process.Start(psiCpu);
                        if (pCpu != null)
                        {
                            string cpuOut = pCpu.StandardOutput.ReadToEnd();
                            pCpu.WaitForExit();
                            pCpu.Dispose();
                            string[] cpuLines = cpuOut.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int ci = 1; ci < cpuLines.Length; ci++)
                            {
                                string[] cpuParts = cpuLines[ci].TrimEnd('\r').Split(',');
                                if (cpuParts.Length >= 2)
                                {
                                    string cpuName = cpuParts[1].Trim();
                                    if (cpuName.Length > 0)
                                    {
                                        lblCpuName.Text = "CPU: " + cpuName;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var psiMem = new ProcessStartInfo("wmic", "OS get TotalVisibleMemorySize,FreePhysicalMemory /format:csv");
                        psiMem.RedirectStandardOutput = true;
                        psiMem.UseShellExecute = false;
                        psiMem.CreateNoWindow = true;
                        var pMem = Process.Start(psiMem);
                        if (pMem != null)
                        {
                            string memOut = pMem.StandardOutput.ReadToEnd();
                            pMem.WaitForExit();
                            pMem.Dispose();
                            string[] memLines = memOut.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int mi = 1; mi < memLines.Length; mi++)
                            {
                                string[] memParts = memLines[mi].TrimEnd('\r').Split(',');
                                if (memParts.Length >= 3)
                                {
                                    long totalKb, freeKb;
                                    if (long.TryParse(memParts[1].Trim(), out freeKb) && long.TryParse(memParts[2].Trim(), out totalKb))
                                    {
                                        double totalGB = totalKb / 1048576.0;
                                        double freeGB = freeKb / 1048576.0;
                                        double usedGB = totalGB - freeGB;
                                        lblRamTotal.Text = "Total RAM: " + totalGB.ToString("F1") + " GB";
                                        lblRamUsed.Text = "Used: " + usedGB.ToString("F1") + " GB";
                                        lblRamAvail.Text = "Available: " + freeGB.ToString("F1") + " GB";
                                        int ramPct = totalGB > 0 ? (int)(usedGB * 100.0 / totalGB) : 0;
                                        barRam.Value = Math.Min(ramPct, 100);
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    lblOsName.Text = "OS: " + Environment.OSVersion.VersionString;
                    lblOsVer.Text = "64-bit: " + Environment.Is64BitOperatingSystem.ToString();
                    TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount);
                    lblUptime.Text = "Uptime: " + uptime.Days + "d " + uptime.Hours + "h " + uptime.Minutes + "m " + uptime.Seconds + "s";

                    StringBuilder diskSb = new StringBuilder();
                    foreach (DriveInfo di in DriveInfo.GetDrives())
                    {
                        if (di.IsReady)
                        {
                            long totalGB = di.TotalSize / 1073741824;
                            long freeGB = di.AvailableFreeSpace / 1073741824;
                            long usedGB = totalGB - freeGB;
                            int pct = totalGB > 0 ? (int)(usedGB * 100 / totalGB) : 0;
                            diskSb.AppendLine(di.Name + " " + di.VolumeLabel + "  " + usedGB + "/" + totalGB + " GB (" + pct + "%)");
                        }
                    }
                    txtDiskInfo.Text = diskSb.ToString();

                    StringBuilder netSb = new StringBuilder();
                    foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                        netSb.AppendLine(nic.Name);
                        netSb.AppendLine("  Type: " + nic.NetworkInterfaceType);
                        netSb.AppendLine("  Speed: " + (nic.Speed / 1000000) + " Mbps");
                        var ipProps = nic.GetIPProperties();
                        foreach (var addr in ipProps.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                netSb.AppendLine("  IPv4: " + addr.Address);
                        }
                        netSb.AppendLine("");
                    }
                    txtNetInfo.Text = netSb.ToString();

                    lblStatus.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss");
                }
                catch { }
            };

            refreshInfo();

            var ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 2000;
            ticker.Tick += (s, e) => refreshInfo();
            ticker.Start();

            f.Controls.Add(tabs);
            f.Controls.Add(lblStatus);
            f.Controls.Add(btnClose);
            f.FormClosed += (s, e) => { ticker.Stop(); ticker.Dispose(); if (perfCpu != null) { try { perfCpu.Dispose(); } catch { } } tabs.Dispose(); lblFont.Dispose(); btnFont.Dispose(); monoFont.Dispose(); ico.Dispose(); lblCpuName.Font.Dispose(); lblCpuUsage.Font.Dispose(); lblRamTotal.Font.Dispose(); };
            f.ShowDialog(this);
            SetStatus("System Info Pro closed");
        }

        void OpenDiskBenchmark()
        {
            var f = new Form();
            f.Text = "Disk Benchmark";
            f.Size = new Size(400, 320);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font bigFont = new Font("Consolas", 18, FontStyle.Bold);

            var lblTitle = new Label();
            lblTitle.Text = "Disk Benchmark (100MB)";
            lblTitle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 170, 255);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(10, 10);

            var lblDrive = new Label();
            lblDrive.Text = "Drive:";
            lblDrive.Font = lblFont;
            lblDrive.ForeColor = Color.White;
            lblDrive.AutoSize = true;
            lblDrive.Location = new Point(10, 45);

            var cmbDrive = new ComboBox();
            cmbDrive.Font = lblFont;
            cmbDrive.Size = new Size(120, 25);
            cmbDrive.Location = new Point(60, 42);
            cmbDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDrive.BackColor = Color.FromArgb(30, 30, 40);
            cmbDrive.ForeColor = Color.White;

            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                if (di.IsReady && di.DriveType == DriveType.Fixed)
                    cmbDrive.Items.Add(di.Name);
            }
            if (cmbDrive.Items.Count > 0) cmbDrive.SelectedIndex = 0;

            var lblWrite = new Label();
            lblWrite.Text = "Write: - MB/s";
            lblWrite.Font = bigFont;
            lblWrite.ForeColor = Color.Lime;
            lblWrite.AutoSize = true;
            lblWrite.Location = new Point(10, 80);

            var lblRead = new Label();
            lblRead.Text = "Read: - MB/s";
            lblRead.Font = bigFont;
            lblRead.ForeColor = Color.Cyan;
            lblRead.AutoSize = true;
            lblRead.Location = new Point(10, 115);

            var barBenchmark = new ProgressBar();
            barBenchmark.Location = new Point(10, 160);
            barBenchmark.Size = new Size(360, 22);
            barBenchmark.Style = ProgressBarStyle.Continuous;

            var lblStatus = new Label();
            lblStatus.Text = "Select drive and click Run Test";
            lblStatus.Font = lblFont;
            lblStatus.ForeColor = Color.FromArgb(100, 100, 120);
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(10, 190);

            var btnRun = new Button();
            btnRun.Text = "Run Test";
            btnRun.Location = new Point(10, 220);
            btnRun.Size = new Size(110, 32);
            btnRun.FlatStyle = FlatStyle.Flat;
            btnRun.BackColor = Color.FromArgb(0, 120, 80);
            btnRun.ForeColor = Color.White;
            btnRun.Font = btnFont;
            btnRun.Cursor = Cursors.Hand;
            btnRun.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(300, 248);
            btnClose.Size = new Size(75, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            btnRun.Click += (s2, e2) =>
            {
                if (cmbDrive.SelectedItem == null) { lblStatus.Text = "Select a drive"; return; }
                string driveLetter = cmbDrive.SelectedItem.ToString();
                string testFile = driveLetter + "gm_benchmark_test.tmp";
                btnRun.Enabled = false;
                barBenchmark.Value = 0;
                lblWrite.Text = "Write: Testing...";
                lblRead.Text = "Read: Testing...";
                lblStatus.Text = "Running write test...";
                Application.DoEvents();

                try
                {
                    int fileSize = 100 * 1024 * 1024;
                    byte[] buffer = new byte[1024 * 1024];
                    Random rng = new Random();

                    Stopwatch swWrite = new Stopwatch();
                    using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int written = 0;
                        swWrite.Start();
                        while (written < fileSize)
                        {
                            rng.NextBytes(buffer);
                            int toWrite = Math.Min(buffer.Length, fileSize - written);
                            fs.Write(buffer, 0, toWrite);
                            written += toWrite;
                            int pct = (int)(written * 100L / fileSize);
                            barBenchmark.Value = Math.Min(pct, 100);
                            Application.DoEvents();
                        }
                        fs.Flush();
                    }
                    swWrite.Stop();
                    barBenchmark.Value = 50;

                    double writeMBs = 0;
                    if (swWrite.Elapsed.TotalSeconds > 0)
                        writeMBs = (fileSize / 1048576.0) / swWrite.Elapsed.TotalSeconds;
                    lblWrite.Text = "Write: " + writeMBs.ToString("F1") + " MB/s";
                    lblStatus.Text = "Running read test...";
                    Application.DoEvents();

                    Stopwatch swRead = new Stopwatch();
                    using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int totalRead = 0;
                        swRead.Start();
                        while (totalRead < fileSize)
                        {
                            int n = fs.Read(buffer, 0, buffer.Length);
                            if (n == 0) break;
                            totalRead += n;
                            int pct = 50 + (int)(totalRead * 50L / fileSize);
                            barBenchmark.Value = Math.Min(pct, 100);
                            Application.DoEvents();
                        }
                    }
                    swRead.Stop();
                    barBenchmark.Value = 100;

                    double readMBs = 0;
                    if (swRead.Elapsed.TotalSeconds > 0)
                        readMBs = (fileSize / 1048576.0) / swRead.Elapsed.TotalSeconds;
                    lblRead.Text = "Read: " + readMBs.ToString("F1") + " MB/s";
                    lblStatus.Text = "Complete! Write: " + writeMBs.ToString("F1") + " MB/s, Read: " + readMBs.ToString("F1") + " MB/s";
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Error: " + ex.Message;
                    lblWrite.Text = "Write: FAILED";
                    lblRead.Text = "Read: FAILED";
                }
                finally
                {
                    try { if (File.Exists(testFile)) File.Delete(testFile); }
                    catch { }
                    btnRun.Enabled = true;
                }
            };

            f.Controls.AddRange(new Control[] { lblTitle, lblDrive, cmbDrive, lblWrite, lblRead, barBenchmark, lblStatus, btnRun, btnClose });
            f.ShowDialog(this);
            lblTitle.Font.Dispose();
            lblFont.Dispose();
            btnFont.Dispose();
            bigFont.Dispose();
            ico.Dispose();
            SetStatus("Disk Benchmark closed");
        }

        void OpenScreenshotOcr()
        {
            var f = new Form();
            f.Text = "Screenshot OCR";
            f.Size = new Size(800, 600);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

            var picImage = new PictureBox();
            picImage.Dock = DockStyle.Fill;
            picImage.BackColor = Color.FromArgb(20, 20, 30);
            picImage.SizeMode = PictureBoxSizeMode.Zoom;

            Bitmap capturedBmp = null;
            bool capturing = false;
            Point dragStart = Point.Empty;

            var lblInstructions = new Label();
            lblInstructions.Text = "Click 'New Capture' then click and drag on screen to select region";
            lblInstructions.Font = lblFont;
            lblInstructions.ForeColor = Color.FromArgb(100, 100, 120);
            lblInstructions.Dock = DockStyle.Top;
            lblInstructions.Height = 25;
            lblInstructions.TextAlign = ContentAlignment.MiddleCenter;

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 45;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnCapture = new Button();
            btnCapture.Text = "New Capture";
            btnCapture.Location = new Point(10, 8);
            btnCapture.Size = new Size(100, 30);
            btnCapture.FlatStyle = FlatStyle.Flat;
            btnCapture.BackColor = Color.FromArgb(0, 120, 80);
            btnCapture.ForeColor = Color.White;
            btnCapture.Font = btnFont;
            btnCapture.Cursor = Cursors.Hand;
            btnCapture.FlatAppearance.BorderSize = 0;

            var btnCopy = new Button();
            btnCopy.Text = "Copy Text";
            btnCopy.Location = new Point(120, 8);
            btnCopy.Size = new Size(100, 30);
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.BackColor = Color.FromArgb(0, 80, 140);
            btnCopy.ForeColor = Color.White;
            btnCopy.Font = btnFont;
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.Enabled = false;

            var btnSave = new Button();
            btnSave.Text = "Save PNG";
            btnSave.Location = new Point(230, 8);
            btnSave.Size = new Size(100, 30);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.BackColor = Color.FromArgb(80, 60, 0);
            btnSave.ForeColor = Color.White;
            btnSave.Font = btnFont;
            btnSave.Cursor = Cursors.Hand;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Enabled = false;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(340, 8);
            btnClose.Size = new Size(75, 30);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            btnCapture.Click += (s2, e2) =>
            {
                capturing = true;
                f.WindowState = FormWindowState.Minimized;
                System.Threading.Thread.Sleep(300);
                f.WindowState = FormWindowState.Normal;
                f.BringToFront();
                lblInstructions.Text = "Click and drag to select a screen region";
            };

            f.MouseDown += (s2, e2) =>
            {
                if (!capturing) return;
                if (e2.Button == MouseButtons.Left)
                {
                    dragStart = f.PointToScreen(e2.Location);
                }
            };

            f.MouseMove += (s2, e2) =>
            {
                if (!capturing) return;
                if (e2.Button == MouseButtons.Left)
                {
                    Point current = f.PointToScreen(e2.Location);
                    int x = Math.Min(dragStart.X, current.X);
                    int y = Math.Min(dragStart.Y, current.Y);
                    int w = Math.Abs(current.X - dragStart.X);
                    int h = Math.Abs(current.Y - dragStart.Y);
                    lblInstructions.Text = "Selection: " + w + "x" + h + " at (" + x + "," + y + ")";
                }
            };

            f.MouseUp += (s2, e2) =>
            {
                if (!capturing) return;
                if (e2.Button == MouseButtons.Left)
                {
                    capturing = false;
                    Point dragEnd = f.PointToScreen(e2.Location);
                    int x = Math.Min(dragStart.X, dragEnd.X);
                    int y = Math.Min(dragStart.Y, dragEnd.Y);
                    int w = Math.Abs(dragEnd.X - dragStart.X);
                    int h = Math.Abs(dragEnd.Y - dragStart.Y);
                    if (w < 10 || h < 10) { lblInstructions.Text = "Selection too small, try again"; return; }
                    try
                    {
                        if (capturedBmp != null) { picImage.Image = null; capturedBmp.Dispose(); capturedBmp = null; }
                        capturedBmp = new Bitmap(w, h);
                        using (var g = Graphics.FromImage(capturedBmp))
                        {
                            g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
                        }
                        picImage.Image = capturedBmp;
                        btnCopy.Enabled = true;
                        btnSave.Enabled = true;
                        lblInstructions.Text = "Captured " + w + "x" + h + " region";
                        SetStatus("Screenshot captured: " + w + "x" + h);
                    }
                    catch (Exception ex) { lblInstructions.Text = "Capture failed: " + ex.Message; }
                }
            };

            btnCopy.Click += (s2, e2) =>
            {
                if (capturedBmp == null) return;
                try
                {
                    Clipboard.SetImage(capturedBmp);
                    lblInstructions.Text = "Image copied to clipboard";
                }
                catch { lblInstructions.Text = "Copy failed"; }
            };

            btnSave.Click += (s2, e2) =>
            {
                if (capturedBmp == null) return;
                string path = PromptSavePath("GM - Save Screenshot", "PNG image", "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                if (path != null)
                {
                    try
                    {
                        capturedBmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        lblInstructions.Text = "Saved: " + Path.GetFileName(path);
                        SetStatus("Screenshot saved");
                    }
                    catch { lblInstructions.Text = "Save failed"; }
                }
            };

            panel.Controls.AddRange(new Control[] { btnCapture, btnCopy, btnSave, btnClose });
            f.Controls.Add(picImage);
            f.Controls.Add(lblInstructions);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { if (capturedBmp != null) { picImage.Image = null; capturedBmp.Dispose(); } lblFont.Dispose(); btnFont.Dispose(); ico.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Screenshot OCR closed");
        }

        void OpenFileLocksmith()
        {
            var f = new Form();
            f.Text = "File Locksmith";
            f.Size = new Size(550, 350);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 10);

            var lblFile = new Label();
            lblFile.Text = "File Path:";
            lblFile.Font = lblFont;
            lblFile.ForeColor = Color.White;
            lblFile.AutoSize = true;
            lblFile.Location = new Point(10, 15);

            var txtPath = new TextBox();
            txtPath.Font = monoFont;
            txtPath.Size = new Size(350, 25);
            txtPath.Location = new Point(80, 12);
            txtPath.BackColor = Color.FromArgb(30, 30, 40);
            txtPath.ForeColor = Color.White;
            txtPath.BorderStyle = BorderStyle.FixedSingle;

            var btnBrowse = new Button();
            btnBrowse.Text = "Browse";
            btnBrowse.Location = new Point(440, 10);
            btnBrowse.Size = new Size(80, 28);
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.BackColor = Color.FromArgb(0, 100, 180);
            btnBrowse.ForeColor = Color.White;
            btnBrowse.Font = btnFont;
            btnBrowse.Cursor = Cursors.Hand;
            btnBrowse.FlatAppearance.BorderSize = 0;

            var btnCheck = new Button();
            btnCheck.Text = "Check";
            btnCheck.Location = new Point(10, 50);
            btnCheck.Size = new Size(100, 32);
            btnCheck.FlatStyle = FlatStyle.Flat;
            btnCheck.BackColor = Color.FromArgb(0, 120, 80);
            btnCheck.ForeColor = Color.White;
            btnCheck.Font = btnFont;
            btnCheck.Cursor = Cursors.Hand;
            btnCheck.FlatAppearance.BorderSize = 0;

            var lblResult = new Label();
            lblResult.Text = "Select a file and click Check";
            lblResult.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblResult.ForeColor = Color.FromArgb(100, 100, 120);
            lblResult.AutoSize = false;
            lblResult.Size = new Size(510, 60);
            lblResult.Location = new Point(10, 95);
            lblResult.TextAlign = ContentAlignment.TopLeft;

            var txtDetails = new TextBox();
            txtDetails.Multiline = true;
            txtDetails.ReadOnly = true;
            txtDetails.ScrollBars = ScrollBars.Vertical;
            txtDetails.Font = monoFont;
            txtDetails.Size = new Size(510, 120);
            txtDetails.Location = new Point(10, 165);
            txtDetails.BackColor = Color.FromArgb(20, 20, 30);
            txtDetails.ForeColor = Color.White;
            txtDetails.BorderStyle = BorderStyle.None;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(440, 295);
            btnClose.Size = new Size(80, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            btnBrowse.Click += (s2, e2) =>
            {
                string path = PromptFilePath("GM - Select File to Check", "Any file");
                if (path != null) { txtPath.Text = path; }
            };

            btnCheck.Click += (s2, e2) =>
            {
                string filePath = txtPath.Text.Trim();
                if (filePath.Length == 0) { lblResult.Text = "Enter or browse for a file path"; lblResult.ForeColor = Color.FromArgb(180, 60, 60); return; }
                if (!File.Exists(filePath)) { lblResult.Text = "File not found: " + filePath; lblResult.ForeColor = Color.FromArgb(180, 60, 60); return; }

                lblResult.Text = "Checking...";
                lblResult.ForeColor = Color.FromArgb(100, 100, 120);
                txtDetails.Text = "";
                Application.DoEvents();

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        lblResult.Text = "File is NOT locked";
                        lblResult.ForeColor = Color.FromArgb(0, 180, 0);
                        txtDetails.Text = "The file was opened exclusively with no contention." + Environment.NewLine + "Path: " + filePath + Environment.NewLine + "Size: " + new FileInfo(filePath).Length + " bytes";
                        SetStatus("File is not locked: " + Path.GetFileName(filePath));
                    }
                }
                catch (IOException)
                {
                    lblResult.Text = "File is LOCKED by another process";
                    lblResult.ForeColor = Color.FromArgb(200, 40, 40);
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("The file could not be opened exclusively.");
                    sb.AppendLine("Path: " + filePath);
                    sb.AppendLine("");
                    sb.AppendLine("Trying to find locking processes via handle...");
                    try
                    {
                        var psi = new ProcessStartInfo("handle.exe", "\"" + filePath + "\"");
                        psi.RedirectStandardOutput = true;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        var proc = Process.Start(psi);
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        proc.Dispose();
                        if (output.Length > 100)
                        {
                            sb.AppendLine(output);
                        }
                        else
                        {
                            sb.AppendLine("handle.exe returned no useful info.");
                            sb.AppendLine("The file is likely locked by another process.");
                        }
                    }
                    catch
                    {
                        sb.AppendLine("handle.exe not available (Sysinternals not installed).");
                        sb.AppendLine("The file is locked but the locking process could not be identified.");
                    }
                    txtDetails.Text = sb.ToString();
                    SetStatus("File is locked: " + Path.GetFileName(filePath));
                }
                catch (UnauthorizedAccessException)
                {
                    lblResult.Text = "Access denied - no permission to read file";
                    lblResult.ForeColor = Color.FromArgb(200, 120, 0);
                    txtDetails.Text = "Path: " + filePath + Environment.NewLine + "The current user does not have permission to open this file.";
                }
                catch (Exception ex)
                {
                    lblResult.Text = "Error checking file";
                    lblResult.ForeColor = Color.FromArgb(200, 120, 0);
                    txtDetails.Text = "Error: " + ex.Message + Environment.NewLine + "Path: " + filePath;
                }
            };

            f.Controls.AddRange(new Control[] { lblFile, txtPath, btnBrowse, btnCheck, lblResult, txtDetails, btnClose });
            f.ShowDialog(this);
            lblResult.Font.Dispose();
            lblFont.Dispose();
            btnFont.Dispose();
            monoFont.Dispose();
            ico.Dispose();
            SetStatus("File Locksmith closed");
        }

        void OpenClipboardMonitor()
        {
            var f = new Form();
            f.Text = "Clipboard Monitor";
            f.Size = new Size(450, 400);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 9);

            var lblInfo = new Label();
            lblInfo.Text = "Clipboard Monitor - Max 20 entries";
            lblInfo.Font = lblFont;
            lblInfo.ForeColor = Color.FromArgb(100, 100, 120);
            lblInfo.Dock = DockStyle.Top;
            lblInfo.Height = 25;
            lblInfo.TextAlign = ContentAlignment.MiddleCenter;

            var list = new ListBox();
            list.Font = monoFont;
            list.BackColor = Color.FromArgb(15, 15, 25);
            list.ForeColor = Color.FromArgb(0, 200, 100);
            list.Dock = DockStyle.Fill;
            list.SelectionMode = SelectionMode.One;

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 40;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnClear = new Button();
            btnClear.Text = "Clear";
            btnClear.Location = new Point(10, 6);
            btnClear.Size = new Size(80, 28);
            btnClear.FlatStyle = FlatStyle.Flat;
            btnClear.BackColor = Color.FromArgb(160, 40, 40);
            btnClear.ForeColor = Color.White;
            btnClear.Font = btnFont;
            btnClear.Cursor = Cursors.Hand;
            btnClear.FlatAppearance.BorderSize = 0;

            var btnCopyBack = new Button();
            btnCopyBack.Text = "Copy Selected";
            btnCopyBack.Location = new Point(100, 6);
            btnCopyBack.Size = new Size(110, 28);
            btnCopyBack.FlatStyle = FlatStyle.Flat;
            btnCopyBack.BackColor = Color.FromArgb(0, 100, 160);
            btnCopyBack.ForeColor = Color.White;
            btnCopyBack.Font = btnFont;
            btnCopyBack.Cursor = Cursors.Hand;
            btnCopyBack.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(350, 6);
            btnClose.Size = new Size(75, 28);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            string lastClip = "";
            var clipTimer = new System.Windows.Forms.Timer();
            clipTimer.Interval = 500;
            clipTimer.Tick += (s2, e2) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string current = Clipboard.GetText();
                        if (current.Length > 0 && current != lastClip)
                        {
                            lastClip = current;
                            string display = current;
                            if (display.Length > 80) display = display.Substring(0, 77) + "...";
                            string timestamp = DateTime.Now.ToString("HH:mm:ss");
                            list.Items.Insert(0, "[" + timestamp + "] " + display);
                            if (list.Items.Count > 20) list.Items.RemoveAt(list.Items.Count - 1);
                        }
                    }
                }
                catch { }
            };

            list.DoubleClick += (s2, e2) =>
            {
                if (list.SelectedItem != null)
                {
                    try
                    {
                        string item = list.SelectedItem.ToString();
                        int bracketEnd = item.IndexOf(']');
                        if (bracketEnd >= 0 && bracketEnd + 2 < item.Length)
                        {
                            string text = item.Substring(bracketEnd + 2);
                            if (text.EndsWith("..."))
                            {
                                foreach (string li in list.Items)
                                {
                                    if (li.Substring(li.IndexOf(']') + 2).StartsWith(text.Substring(0, text.Length - 3)))
                                    {
                                        Clipboard.SetText(li.Substring(li.IndexOf(']') + 2));
                                        return;
                                    }
                                }
                            }
                            Clipboard.SetText(text);
                        }
                    }
                    catch { }
                }
            };

            btnClear.Click += (s2, e2) => { list.Items.Clear(); lastClip = ""; };
            btnCopyBack.Click += (s2, e2) =>
            {
                if (list.SelectedItem != null)
                {
                    try
                    {
                        string item = list.SelectedItem.ToString();
                        int bracketEnd = item.IndexOf(']');
                        if (bracketEnd >= 0 && bracketEnd + 2 < item.Length)
                            Clipboard.SetText(item.Substring(bracketEnd + 2));
                    }
                    catch { }
                }
            };

            clipTimer.Start();

            panel.Controls.AddRange(new Control[] { btnClear, btnCopyBack, btnClose });
            f.Controls.Add(list);
            f.Controls.Add(lblInfo);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { clipTimer.Stop(); clipTimer.Dispose(); lblFont.Dispose(); btnFont.Dispose(); monoFont.Dispose(); ico.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Clipboard Monitor closed");
        }

        void OpenSleepTimer()
        {
            var f = new Form();
            f.Text = "Sleep/Wake Timer";
            f.Size = new Size(400, 480);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);
            Font bigFont = new Font("Consolas", 14, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 10);

            var lblSleepTitle = new Label();
            lblSleepTitle.Text = "Sleep / Shutdown Controls";
            lblSleepTitle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblSleepTitle.ForeColor = Color.FromArgb(0, 170, 255);
            lblSleepTitle.AutoSize = true;
            lblSleepTitle.Location = new Point(10, 10);

            var lblMin = new Label();
            lblMin.Text = "Minutes:";
            lblMin.Font = lblFont;
            lblMin.ForeColor = Color.White;
            lblMin.AutoSize = true;
            lblMin.Location = new Point(10, 45);

            var txtMin = new TextBox();
            txtMin.Font = bigFont;
            txtMin.Size = new Size(80, 30);
            txtMin.Location = new Point(85, 40);
            txtMin.Text = "30";
            txtMin.TextAlign = HorizontalAlignment.Center;
            txtMin.BackColor = Color.FromArgb(30, 30, 40);
            txtMin.ForeColor = Color.White;
            txtMin.BorderStyle = BorderStyle.FixedSingle;

            var btnSleep = new Button();
            btnSleep.Text = "Sleep PC";
            btnSleep.Location = new Point(10, 85);
            btnSleep.Size = new Size(90, 32);
            btnSleep.FlatStyle = FlatStyle.Flat;
            btnSleep.BackColor = Color.FromArgb(0, 100, 180);
            btnSleep.ForeColor = Color.White;
            btnSleep.Font = btnFont;
            btnSleep.Cursor = Cursors.Hand;
            btnSleep.FlatAppearance.BorderSize = 0;

            var btnHibernate = new Button();
            btnHibernate.Text = "Hibernate PC";
            btnHibernate.Location = new Point(110, 85);
            btnHibernate.Size = new Size(100, 32);
            btnHibernate.FlatStyle = FlatStyle.Flat;
            btnHibernate.BackColor = Color.FromArgb(0, 80, 140);
            btnHibernate.ForeColor = Color.White;
            btnHibernate.Font = btnFont;
            btnHibernate.Cursor = Cursors.Hand;
            btnHibernate.FlatAppearance.BorderSize = 0;

            var btnRestart = new Button();
            btnRestart.Text = "Restart PC";
            btnRestart.Location = new Point(10, 125);
            btnRestart.Size = new Size(90, 32);
            btnRestart.FlatStyle = FlatStyle.Flat;
            btnRestart.BackColor = Color.FromArgb(200, 120, 0);
            btnRestart.ForeColor = Color.White;
            btnRestart.Font = btnFont;
            btnRestart.Cursor = Cursors.Hand;
            btnRestart.FlatAppearance.BorderSize = 0;

            var btnShutdown = new Button();
            btnShutdown.Text = "Shutdown PC";
            btnShutdown.Location = new Point(110, 125);
            btnShutdown.Size = new Size(100, 32);
            btnShutdown.FlatStyle = FlatStyle.Flat;
            btnShutdown.BackColor = Color.FromArgb(200, 40, 40);
            btnShutdown.ForeColor = Color.White;
            btnShutdown.Font = btnFont;
            btnShutdown.Cursor = Cursors.Hand;
            btnShutdown.FlatAppearance.BorderSize = 0;

            var btnCancel = new Button();
            btnCancel.Text = "Cancel Shutdown";
            btnCancel.Location = new Point(220, 85);
            btnCancel.Size = new Size(120, 32);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.BackColor = Color.FromArgb(80, 80, 100);
            btnCancel.ForeColor = Color.White;
            btnCancel.Font = btnFont;
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.FlatAppearance.BorderSize = 0;

            var lblCountdown = new Label();
            lblCountdown.Text = "";
            lblCountdown.Font = new Font("Consolas", 16, FontStyle.Bold);
            lblCountdown.ForeColor = Color.Lime;
            lblCountdown.AutoSize = false;
            lblCountdown.Size = new Size(360, 35);
            lblCountdown.Location = new Point(10, 170);
            lblCountdown.TextAlign = ContentAlignment.MiddleCenter;

            var lblWakeTitle = new Label();
            lblWakeTitle.Text = "Wake Timer";
            lblWakeTitle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblWakeTitle.ForeColor = Color.FromArgb(0, 200, 100);
            lblWakeTitle.AutoSize = true;
            lblWakeTitle.Location = new Point(10, 220);

            var lblWakeTime = new Label();
            lblWakeTime.Text = "Wake at:";
            lblWakeTime.Font = lblFont;
            lblWakeTime.ForeColor = Color.White;
            lblWakeTime.AutoSize = true;
            lblWakeTime.Location = new Point(10, 255);

            var dtpWake = new DateTimePicker();
            dtpWake.Font = monoFont;
            dtpWake.Size = new Size(200, 25);
            dtpWake.Location = new Point(80, 252);
            dtpWake.Format = DateTimePickerFormat.Time;
            dtpWake.ShowUpDown = true;
            dtpWake.Value = DateTime.Now.AddHours(1);

            var btnSetWake = new Button();
            btnSetWake.Text = "Set Wake Timer";
            btnSetWake.Location = new Point(290, 250);
            btnSetWake.Size = new Size(90, 28);
            btnSetWake.FlatStyle = FlatStyle.Flat;
            btnSetWake.BackColor = Color.FromArgb(0, 140, 80);
            btnSetWake.ForeColor = Color.White;
            btnSetWake.Font = btnFont;
            btnSetWake.Cursor = Cursors.Hand;
            btnSetWake.FlatAppearance.BorderSize = 0;

            var lblWakeStatus = new Label();
            lblWakeStatus.Text = "";
            lblWakeStatus.Font = lblFont;
            lblWakeStatus.ForeColor = Color.FromArgb(100, 100, 120);
            lblWakeStatus.AutoSize = true;
            lblWakeStatus.Location = new Point(10, 290);

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(300, 410);
            btnClose.Size = new Size(75, 32);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            int countdownSeconds = 0;
            var ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 1000;
            ticker.Tick += (s2, e2) =>
            {
                if (countdownSeconds > 0)
                {
                    countdownSeconds--;
                    int hrs = countdownSeconds / 3600;
                    int mins = (countdownSeconds % 3600) / 60;
                    int secs = countdownSeconds % 60;
                    lblCountdown.Text = string.Format("{0:00}:{1:00}:{2:00}", hrs, mins, secs);
                    if (countdownSeconds <= 0) { lblCountdown.Text = "Time's up!"; ticker.Stop(); }
                }
            };

            btnSleep.Click += (s2, e2) =>
            {
                if (MessageBox.Show("Put PC to sleep now?", "Sleep/Wake Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true, UseShellExecute = false });
                    SetStatus("PC sleeping");
                }
                catch { MessageBox.Show("Could not put PC to sleep.", "GM"); }
            };

            btnHibernate.Click += (s2, e2) =>
            {
                if (MessageBox.Show("Hibernate PC now?", "Sleep/Wake Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 1,0,0") { CreateNoWindow = true, UseShellExecute = false });
                    SetStatus("PC hibernating");
                }
                catch { MessageBox.Show("Could not hibernate PC.", "GM"); }
            };

            btnRestart.Click += (s2, e2) =>
            {
                int mins;
                if (!int.TryParse(txtMin.Text, out mins) || mins <= 0 || mins > 1440) { lblCountdown.Text = "Enter 1-1440 minutes"; return; }
                if (MessageBox.Show("Restart PC in " + mins + " minute(s)?", "Sleep/Wake Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                countdownSeconds = mins * 60; ticker.Start();
                lblCountdown.ForeColor = Color.FromArgb(255, 165, 0);
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t " + countdownSeconds) { CreateNoWindow = true, UseShellExecute = false });
                    SetStatus("Restart scheduled in " + mins + " min");
                }
                catch { lblCountdown.Text = "Failed to schedule"; ticker.Stop(); }
            };

            btnShutdown.Click += (s2, e2) =>
            {
                int mins;
                if (!int.TryParse(txtMin.Text, out mins) || mins <= 0 || mins > 1440) { lblCountdown.Text = "Enter 1-1440 minutes"; return; }
                if (MessageBox.Show("Shutdown PC in " + mins + " minute(s)?", "Sleep/Wake Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                countdownSeconds = mins * 60; ticker.Start();
                lblCountdown.ForeColor = Color.Lime;
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t " + countdownSeconds) { CreateNoWindow = true, UseShellExecute = false });
                    SetStatus("Shutdown scheduled in " + mins + " min");
                }
                catch { lblCountdown.Text = "Failed to schedule"; ticker.Stop(); }
            };

            btnCancel.Click += (s2, e2) =>
            {
                ticker.Stop();
                countdownSeconds = 0; lblCountdown.Text = "Cancelled";
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false });
                    SetStatus("Shutdown cancelled");
                }
                catch { }
            };

            btnSetWake.Click += (s2, e2) =>
            {
                try
                {
                    DateTime wakeTime = dtpWake.Value;
                    if (wakeTime <= DateTime.Now) wakeTime = wakeTime.AddDays(1);
                    TimeSpan delay = wakeTime - DateTime.Now;
                    int seconds = (int)delay.TotalSeconds;
                    var psi = new ProcessStartInfo("powershell", "-Command \"Add-Type -Path 'C:\\Windows\\System32\\powrprof.dll' 2>$null; [System.Windows.Forms.Application]::SetSuspendState('Sleep', $false, $false)\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    lblWakeStatus.Text = "Wake set for " + wakeTime.ToString("HH:mm:ss") + " (" + (int)delay.TotalHours + "h " + delay.Minutes + "m)";
                    lblWakeStatus.ForeColor = Color.FromArgb(0, 180, 0);
                    SetStatus("Wake timer set for " + wakeTime.ToString("HH:mm:ss"));
                }
                catch (Exception ex) { lblWakeStatus.Text = "Failed: " + ex.Message; lblWakeStatus.ForeColor = Color.FromArgb(180, 60, 60); }
            };

            f.Controls.AddRange(new Control[] { lblSleepTitle, lblMin, txtMin, btnSleep, btnHibernate, btnRestart, btnShutdown, btnCancel, lblCountdown, lblWakeTitle, lblWakeTime, dtpWake, btnSetWake, lblWakeStatus, btnClose });
            f.ShowDialog(this);
            ticker.Stop(); ticker.Dispose();
            lblSleepTitle.Font.Dispose();
            lblCountdown.Font.Dispose();
            lblWakeTitle.Font.Dispose();
            lblFont.Dispose(); btnFont.Dispose(); bigFont.Dispose(); monoFont.Dispose(); ico.Dispose();
            SetStatus("Sleep/Wake Timer closed");
        }

        void OpenCrapwareDetector()
        {
            var f = new Form();
            f.Text = "Crapware Detector";
            f.Size = new Size(700, 500);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;

            Font lblFont = new Font("Segoe UI", 9);
            Font btnFont = new Font("Segoe UI", 9, FontStyle.Bold);

            string[] crapware = { "mcafee", "norton", "avg", "bing bar", "yahoo toolbar", "ask toolbar", "babylon", "conduit", "sweetim", "coolwebsearch", "wildtangent", "weatherbug", "snap.do", "delta search", "delta toolbar", "trovi", "babylon toolbar", "sweetim toolbar", "ask search", "search protect", "web assistant", "price finder", "superfish", "duit search", "amisite", "searchmine", "advanced systemcare", "iobit", "cleanmyppc", "pc speedup", "speedupmypc", "registry mechanic", "pc cleaner", "driver genius", "driver booster", "smart price", "coupon companion", "dealply", "inbox Toolbar", "amazon browser bar", "ebay toolbar", "avast safeprice", "kaspersky vpn", "nordvpn extension", "hotspot shield", "vyprvpn extension" };
            string[] highRisk = { "mcafee", "norton", "avg", "bing bar", "yahoo toolbar", "ask toolbar", "babylon", "conduit", "sweetim", "coolwebsearch", "wildtangent", "superfish", "duit search", "amisite", "searchmine", "search protect", "delta search", "trovi", "web assistant" };
            string[] mediumRisk = { "weatherbug", "snap.do", "advanced systemcare", "iobit", "cleanmyppc", "pc speedup", "speedupmypc", "registry mechanic", "pc cleaner", "driver genius", "driver booster", "smart price", "coupon companion", "dealply", "inbox toolbar", "amazon browser bar", "ebay toolbar" };

            var lblInfo = new Label();
            lblInfo.Text = "Scanning installed programs for known crapware...";
            lblInfo.Font = lblFont;
            lblInfo.ForeColor = Color.FromArgb(100, 100, 120);
            lblInfo.Dock = DockStyle.Top;
            lblInfo.Height = 25;
            lblInfo.TextAlign = ContentAlignment.MiddleCenter;

            var grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.FromArgb(15, 15, 25);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 20, 35);
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 100, 160);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 40);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.EnableHeadersVisualStyles = false;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            grid.Columns.Add("Name", "Program Name");
            grid.Columns.Add("Publisher", "Publisher");
            grid.Columns.Add("Date", "Install Date");
            grid.Columns.Add("Risk", "Risk Level");
            grid.Columns["Name"].FillWeight = 40;
            grid.Columns["Publisher"].FillWeight = 30;
            grid.Columns["Date"].FillWeight = 15;
            grid.Columns["Risk"].FillWeight = 15;

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 45;
            panel.BackColor = Color.FromArgb(20, 20, 30);

            var btnSelectAll = new Button();
            btnSelectAll.Text = "Select All";
            btnSelectAll.Location = new Point(10, 8);
            btnSelectAll.Size = new Size(80, 28);
            btnSelectAll.FlatStyle = FlatStyle.Flat;
            btnSelectAll.BackColor = Color.FromArgb(0, 100, 160);
            btnSelectAll.ForeColor = Color.White;
            btnSelectAll.Font = btnFont;
            btnSelectAll.Cursor = Cursors.Hand;
            btnSelectAll.FlatAppearance.BorderSize = 0;

            var btnDeselectAll = new Button();
            btnDeselectAll.Text = "Deselect All";
            btnDeselectAll.Location = new Point(100, 8);
            btnDeselectAll.Size = new Size(90, 28);
            btnDeselectAll.FlatStyle = FlatStyle.Flat;
            btnDeselectAll.BackColor = Color.FromArgb(80, 80, 100);
            btnDeselectAll.ForeColor = Color.White;
            btnDeselectAll.Font = btnFont;
            btnDeselectAll.Cursor = Cursors.Hand;
            btnDeselectAll.FlatAppearance.BorderSize = 0;

            var btnUninstall = new Button();
            btnUninstall.Text = "Uninstall Selected";
            btnUninstall.Location = new Point(200, 8);
            btnUninstall.Size = new Size(120, 28);
            btnUninstall.FlatStyle = FlatStyle.Flat;
            btnUninstall.BackColor = Color.FromArgb(200, 40, 40);
            btnUninstall.ForeColor = Color.White;
            btnUninstall.Font = btnFont;
            btnUninstall.Cursor = Cursors.Hand;
            btnUninstall.FlatAppearance.BorderSize = 0;

            var btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Location = new Point(590, 8);
            btnClose.Size = new Size(75, 28);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(80, 80, 100);
            btnClose.ForeColor = Color.White;
            btnClose.Font = btnFont;
            btnClose.Cursor = Cursors.Hand;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();

            int foundCount = 0;
            try
            {
                string[] regPaths = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
                foreach (string regPath in regPaths)
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        if (key == null) continue;
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    if (subKey == null) continue;
                                    string displayName = subKey.GetValue("DisplayName") as string;
                                    if (displayName == null || displayName.Length == 0) continue;
                                    string publisher = subKey.GetValue("Publisher") as string ?? "";
                                    string installDate = subKey.GetValue("InstallDate") as string ?? "";
                                    string nameLower = displayName.ToLower();
                                    string risk = "Safe";
                                    Color riskColor = Color.FromArgb(0, 180, 0);

                                    foreach (string kw in highRisk)
                                    {
                                        if (nameLower.Contains(kw)) { risk = "High"; riskColor = Color.FromArgb(200, 40, 40); break; }
                                    }
                                    if (risk == "Safe")
                                    {
                                        foreach (string kw in mediumRisk)
                                        {
                                            if (nameLower.Contains(kw)) { risk = "Medium"; riskColor = Color.FromArgb(200, 165, 0); break; }
                                        }
                                    }
                                    if (risk == "Safe")
                                    {
                                        foreach (string kw in crapware)
                                        {
                                            if (nameLower.Contains(kw)) { risk = "Medium"; riskColor = Color.FromArgb(200, 165, 0); break; }
                                        }
                                    }

                                    if (risk != "Safe")
                                    {
                                        int idx = grid.Rows.Add(displayName, publisher, installDate, risk);
                                        grid.Rows[idx].Cells[3].Style.ForeColor = riskColor;
                                        grid.Rows[idx].Cells[3].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
                                        foundCount++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            lblInfo.Text = "Found " + foundCount + " potentially unwanted programs";
            if (foundCount > 0) lblInfo.ForeColor = Color.FromArgb(200, 120, 0);

            btnSelectAll.Click += (s2, e2) => { grid.SelectAll(); };
            btnDeselectAll.Click += (s2, e2) => { grid.ClearSelection(); };
            btnUninstall.Click += (s2, e2) =>
            {
                if (grid.SelectedRows.Count == 0) { MessageBox.Show("Select programs to uninstall first.", "Crapware Detector"); return; }
                if (MessageBox.Show("Open Control Panel to uninstall " + grid.SelectedRows.Count + " selected program(s)?", "Crapware Detector", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });
                    SetStatus("Control Panel opened for uninstallation");
                }
                catch { MessageBox.Show("Could not open Control Panel.", "GM"); }
            };

            panel.Controls.AddRange(new Control[] { btnSelectAll, btnDeselectAll, btnUninstall, btnClose });
            f.Controls.Add(grid);
            f.Controls.Add(lblInfo);
            f.Controls.Add(panel);
            f.FormClosed += (s2, e2) => { lblFont.Dispose(); btnFont.Dispose(); ico.Dispose(); grid.DefaultCellStyle.Font.Dispose(); grid.ColumnHeadersDefaultCellStyle.Font.Dispose(); };
            f.ShowDialog(this);
            SetStatus("Crapware Detector closed");
        }

        void OpenDnsChanger()
        {
            var f = new Form();
            f.Text = "GM - DNS Changer";
            f.Size = new Size(400, 250);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "DNS Changer - Coming soon", Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(80, 80) };
            var btnClose = new Button { Text = "Close", Location = new Point(150, 130), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.AddRange(new Control[] { lbl, btnClose });
            f.ShowDialog(this);
            lbl.Font.Dispose();
            btnClose.Font.Dispose();
            ico.Dispose();
            SetStatus("DNS Changer closed");
        }

        void OpenNetConnections()
        {
            var f = new Form();
            f.Text = "GM - Net Connections";
            f.Size = new Size(700, 500);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill };
            var btnClose = new Button { Text = "Close", Location = new Point(600, 8), Size = new Size(70, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano");
                psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                var p = Process.Start(psi);
                if (p != null) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); foreach (string line in output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) list.Items.Add(line.TrimEnd('\r')); }
            }
            catch { list.Items.Add("Error loading connections"); }
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(20, 20, 30) };
            panel.Controls.Add(btnClose);
            f.Controls.Add(list);
            f.Controls.Add(panel);
            f.ShowDialog(this);
            list.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Net Connections closed");
        }

        void OpenTraceroute()
        {
            var f = new Form();
            f.Text = "GM - Traceroute";
            f.Size = new Size(600, 500);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var txtHost = new TextBox { Font = new Font("Consolas", 10), Size = new Size(300, 28), Location = new Point(10, 10), BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "8.8.8.8" };
            var btnTrace = new Button { Text = "Trace", Location = new Point(320, 10), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnTrace.FlatAppearance.BorderSize = 0;
            var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.FromArgb(0, 200, 100), Location = new Point(10, 48), Size = new Size(560, 380) };
            var btnClose = new Button { Text = "Close", Location = new Point(410, 10), Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            btnTrace.Click += (s2, e2) =>
            {
                list.Items.Clear();
                try
                {
                    string host = txtHost.Text.Trim();
                    if (string.IsNullOrEmpty(host)) return;
                    var psi = new ProcessStartInfo("tracert", "-d " + host);
                    psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    var p = Process.Start(psi);
                    if (p != null) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); foreach (string line in output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) list.Items.Add(line.TrimEnd('\r')); }
                }
                catch { list.Items.Add("Error running traceroute"); }
            };
            f.Controls.AddRange(new Control[] { txtHost, btnTrace, btnClose, list });
            f.ShowDialog(this);
            txtHost.Font.Dispose(); list.Font.Dispose(); btnTrace.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Traceroute closed");
        }

        void OpenIpConfig()
        {
            var f = new Form();
            f.Text = "GM - IP Config";
            f.Size = new Size(600, 450);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var txt = new TextBox { Multiline = true, ReadOnly = true, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.Lime, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both };
            try
            {
                var psi = new ProcessStartInfo("ipconfig", "/all");
                psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                var p = Process.Start(psi);
                if (p != null) { txt.Text = p.StandardOutput.ReadToEnd(); p.WaitForExit(); p.Dispose(); }
            }
            catch { txt.Text = "Error running ipconfig"; }
            var btnClose = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.Add(txt);
            f.Controls.Add(btnClose);
            f.ShowDialog(this);
            txt.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("IP Config closed");
        }

        void OpenFirewallRules()
        {
            var f = new Form();
            f.Text = "GM - Firewall Rules";
            f.Size = new Size(400, 250);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "Firewall Rules - Coming soon", Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(80, 80) };
            var btnClose = new Button { Text = "Close", Location = new Point(150, 130), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.AddRange(new Control[] { lbl, btnClose });
            f.ShowDialog(this);
            lbl.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Firewall Rules closed");
        }

        void OpenBandwidthTest()
        {
            var f = new Form();
            f.Text = "GM - Bandwidth Test";
            f.Size = new Size(400, 250);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "Bandwidth Test - Coming soon", Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(80, 80) };
            var btnClose = new Button { Text = "Close", Location = new Point(150, 130), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.AddRange(new Control[] { lbl, btnClose });
            f.ShowDialog(this);
            lbl.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Bandwidth Test closed");
        }

        void OpenWindowInspector()
        {
            var f = new Form();
            f.Text = "GM - Window Inspector";
            f.Size = new Size(500, 400);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var list = new ListBox { Font = new Font("Consolas", 9), BackColor = Color.FromArgb(20, 20, 35), ForeColor = Color.FromArgb(0, 200, 100), Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
            var btnRefresh = new Button { Text = "Refresh", Location = new Point(10, 8), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 100, 160), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRefresh.FlatAppearance.BorderSize = 0;
            var btnClose = new Button { Text = "Close", Location = new Point(400, 8), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            Action refresh = () =>
            {
                list.Items.Clear();
                foreach (Process p in Process.GetProcesses())
                {
                    try { if (p.MainWindowTitle.Length > 0) list.Items.Add(p.Id + " - " + p.ProcessName + " - " + p.MainWindowTitle); } catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            };
            btnRefresh.Click += (s2, e2) => refresh();
            refresh();
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(20, 20, 30) };
            panel.Controls.AddRange(new Control[] { btnRefresh, btnClose });
            f.Controls.Add(list);
            f.Controls.Add(panel);
            f.ShowDialog(this);
            list.Font.Dispose(); btnRefresh.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Window Inspector closed");
        }

        void OpenScreenRuler()
        {
            var f = new Form();
            f.Text = "GM - Screen Ruler";
            f.Size = new Size(600, 100);
            f.FormBorderStyle = FormBorderStyle.None;
            f.BackColor = Color.FromArgb(30, 30, 40);
            f.TopMost = true;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(100, 100);
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "Drag to measure | Esc to close", Font = new Font("Consolas", 12, FontStyle.Bold), ForeColor = Color.Lime, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            bool dragging = false; int sx = 0, sy = 0;
            f.MouseDown += (s, e) => { dragging = true; sx = e.X; sy = e.Y; };
            f.MouseMove += (s, e) => { if (dragging) { int w = Math.Abs(e.X - sx); int h = Math.Abs(e.Y - sy); lbl.Text = w + " x " + h + " px"; } };
            f.MouseUp += (s, e) => { dragging = false; };
            f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
            f.Controls.Add(lbl);
            f.FormClosed += (s2, e2) => { lbl.Font.Dispose(); ico.Dispose(); };
            f.Show();
            SetStatus("Screen Ruler opened");
        }

        void OpenProcessWatcher()
        {
            var f = new Form();
            f.Text = "GM - Process Watcher";
            f.Size = new Size(500, 400);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "Process Watcher - Coming soon", Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(120, 150) };
            var btnClose = new Button { Text = "Close", Location = new Point(200, 200), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.AddRange(new Control[] { lbl, btnClose });
            f.ShowDialog(this);
            lbl.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Process Watcher closed");
        }

        void OpenQuickLauncher()
        {
            var f = new Form();
            f.Text = "GM - Quick Launcher";
            f.Size = new Size(400, 300);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.BackColor = Color.FromArgb(15, 15, 25);
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = this.Font;
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            f.Icon = ico;
            var lbl = new Label { Text = "Quick Launcher - Coming soon", Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(120, 120, 140), AutoSize = true, Location = new Point(80, 100) };
            var btnClose = new Button { Text = "Close", Location = new Point(150, 150), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s2, e2) => f.Close();
            f.Controls.AddRange(new Control[] { lbl, btnClose });
            f.ShowDialog(this);
            lbl.Font.Dispose(); btnClose.Font.Dispose(); ico.Dispose();
            SetStatus("Quick Launcher closed");
        }

        void OpenAlwaysOnTop()
        {
            try
            {
                IntPtr hWnd = FindWindow(null, null);
                if (hWnd != IntPtr.Zero)
                {
                    SetWindowPos(hWnd, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
                    SetStatus("Always-on-top toggled");
                }
            }
            catch { SetStatus("Failed to toggle always-on-top"); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                if (tips != null) { tips.Dispose(); tips = null; }
                if (titleFont != null) { titleFont.Dispose(); titleFont = null; }
                if (subFont != null) { subFont.Dispose(); subFont = null; }
                if (btnFont != null) { btnFont.Dispose(); btnFont = null; }
                if (footFont != null) { footFont.Dispose(); footFont = null; }
                if (formFont != null) { formFont.Dispose(); formFont = null; }
                if (statusFont != null) { statusFont.Dispose(); statusFont = null; }
                if (ownIcon && this.Icon != null) { this.Icon.Dispose(); this.Icon = null; }
            }
            base.Dispose(disposing);
        }
    }
}

