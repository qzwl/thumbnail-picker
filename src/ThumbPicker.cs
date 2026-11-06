using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ThumbPicker
{
    // ---------- 注册表与缩略图引擎 -------------------------------
    static class Thumbs
    {
        public const string IFace = "{e357fccd-a995-4576-b01f-234630154e96}";  // IThumbnailProvider
        public const string BuiltInClsid = "{C7657C4A-9F68-40fa-A4DF-96BC08EB3551}";

        public static string UserKeyPath(string ext) { return @"Software\Classes\" + ext + @"\ShellEx\" + IFace; }
        public static string MachineKeyPath(string ext) { return @"SOFTWARE\Classes\" + ext + @"\ShellEx\" + IFace; }

        public static string UserOverride(string ext)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UserKeyPath(ext)))
            {
                if (k == null) return null;
                object v = k.GetValue(null);
                return v == null ? null : v.ToString();
            }
        }

        public static string MachineHandler(string ext)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(MachineKeyPath(ext)))
            {
                if (k == null) return null;
                object v = k.GetValue(null);
                return v == null ? null : v.ToString();
            }
        }

        public static string Effective(string ext)
        {
            string u = UserOverride(ext);
            return (u != null && u.Length > 0) ? u : MachineHandler(ext);
        }

        public static bool SetHandler(string ext, string clsid)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UserKeyPath(ext), RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (k == null) return false;
                    k.SetValue(null, clsid, RegistryValueKind.String);
                }
                return true;
            }
            catch { return false; }
        }

        public static bool ClearOverride(string ext)
        {
            try
            {
                using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                {
                    if (classes == null) return false;
                    using (RegistryKey ekey = classes.OpenSubKey(ext, true))
                    {
                        if (ekey == null) return true;
                        try { ekey.DeleteSubKeyTree(@"ShellEx\" + IFace, false); } catch { }
                    }
                    // 若该扩展名键已经空了（不是用户原有的），顺手清掉
                    using (RegistryKey ekey2 = classes.OpenSubKey(ext, false))
                    {
                        if (ekey2 != null && ekey2.SubKeyCount == 0 && ekey2.ValueCount == 0)
                        {
                            ekey2.Close();
                            try { classes.DeleteSubKeyTree(ext, false); } catch { }
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        // 常见“应当支持缩略图”的格式，即使当前没有注册也要列出来
        static readonly string[] KnownFormats = new string[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif",
            ".avif", ".svg", ".ico", ".psd", ".jxl", ".raw", ".pdf",
            ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm",
            ".mp4", ".mov", ".avi", ".mkv", ".wmv"
        };

        public static readonly string[] ImageFormats = new string[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif",
            ".avif", ".svg", ".ico", ".psd", ".jxl", ".raw", ".dds", ".tga", ".pcx"
        };

        public static List<string> AllFormats(out Dictionary<string, string> machineMap)
        {
            machineMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey hkcr = Registry.ClassesRoot)
                {
                    foreach (string name in hkcr.GetSubKeyNames())
                    {
                        if (!name.StartsWith(".")) continue;
                        bool has = false;
                        using (RegistryKey um = Registry.CurrentUser.OpenSubKey(UserKeyPath(name))) { if (um != null) has = true; }
                        string mach = null;
                        using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MachineKeyPath(name))) { if (mm != null) { object o = mm.GetValue(null); if (o != null) mach = o.ToString(); } }
                        if (mach != null) { machineMap[name] = mach; has = true; }
                        if (has) set.Add(name);
                    }
                }
            }
            catch { }
            foreach (string k in KnownFormats) set.Add(k);
            return new List<string>(set);
        }

        // 扫描“已安装但当前没被任何格式用到”的缩略图组件，让用户也能选到它们
        public static List<string> FindInstalledThumbProviders()
        {
            var found = new List<string>();
            string[] roots = new string[] { @"SOFTWARE\Classes\CLSID", @"SOFTWARE\Classes\WOW6432Node\CLSID" };
            foreach (string r in roots)
            {
                RegistryKey k = null;
                try
                {
                    k = Registry.LocalMachine.OpenSubKey(r);
                    if (k == null) continue;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        if (!sub.StartsWith("{")) continue;
                        bool looksThumb = false;
                        using (RegistryKey ip = k.OpenSubKey(sub + @"\InProcServer32"))
                        {
                            if (ip != null)
                            {
                                object dll = ip.GetValue(null);
                                if (dll != null && dll.ToString().IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) looksThumb = true;
                            }
                        }
                        if (!looksThumb)
                        {
                            using (RegistryKey ck = k.OpenSubKey(sub))
                            {
                                if (ck != null)
                                {
                                    object nm = ck.GetValue(null);
                                    if (nm != null && nm.ToString().IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) looksThumb = true;
                                }
                            }
                        }
                        if (looksThumb && !found.Contains(sub)) found.Add(sub);
                    }
                }
                catch { }
                finally { if (k != null) k.Close(); }
            }
            return found;
        }

        public class Provider
        {
            public string Clsid;
            public string Name;
            public string Dll;
            public string ProductName;
            public bool DllExists;
            public bool Registered;
            public int UsedBy;
            public string SampleExts;
            public string Display;
            public List<string> UsedExts = new List<string>();

            public string Detail()
            {
                var sb = new StringBuilder();
                sb.AppendLine("名称：" + Name);
                sb.AppendLine("CLSID：" + Clsid);
                sb.AppendLine("组件：" + (Dll == null ? "（注册表未记录路径）" : Dll));
                sb.AppendLine("状态：" + (!Registered ? "未注册（选择它会失败）" : (DllExists ? "可用" : "组件文件不存在（选择它会失败）")));
                sb.AppendLine("覆盖格式：" + (UsedBy > 0 ? UsedBy + " 个（" + (SampleExts ?? "") + "）" : "当前没有被任何格式使用"));
                sb.Append("提示：系统内置照片引擎只处理位图（jpg/png/bmp/gif/tif/webp 等），PDF、Office、视频请保留各自的专用组件");
                return sb.ToString();
            }

            public override string ToString() { return Display ?? Name; }
        }

        // 常见 DLL 的友好名称，避免显示成 “Microsoft® Windows® Operating System”
        public static string FriendlyName(string dll, string fallback)
        {
            if (string.IsNullOrEmpty(dll)) return fallback;
            string fn = Path.GetFileName(dll).ToLowerInvariant();
            switch (fn)
            {
                case "shell32.dll": return "Windows 系统（视频/音频）";
                case "photometadatahandler.dll": return "Windows 系统（照片）";
                case "windows.storage.dll": return "Windows 快捷方式";
                case "msoshext.dll": return "Microsoft Office";
                case "2345imagethumb64.dll": return "2345看图王";
                case "guohe-view-thumb.dll": return "果核看图";
                case "3mfthumbnailprovider.dll": return "Windows 3D 模型";
                case "ms3dthumb.dll": return "Windows 3D 模型";
                case "ms3dthumbnailprovider.dll": return "Windows 3D 模型";
                case "thumbcache.dll": return "Windows 缩略图缓存";
                default:
                    if (fn.StartsWith("powertoys.")) return "PowerToys 缩略图组件";
                    if (fn.Contains("mso")) return "Microsoft Office";
                    return string.IsNullOrWhiteSpace(fallback) ? fn : fallback;
            }
        }

        // 按软件（DLL 路径）合并后的展示单元
        public class Product
        {
            public string Dll;
            public string Name;
            public bool Ok = true;
            public List<Provider> Items = new List<Provider>();
            public List<string> Exts = new List<string>();
            public string Display;

            public override string ToString() { return Display ?? Name; }

            public string Detail()
            {
                var sb = new StringBuilder();
                sb.AppendLine("软件：" + Name);
                sb.AppendLine("组件：" + (Dll == null ? "（未知）" : Dll));
                sb.AppendLine("状态：" + (Ok ? "可用" : "部分组件缺失"));
                sb.Append("覆盖格式：" + (Exts.Count > 0
                    ? Exts.Count + " 个　" + ShortExts(Exts, 150)
                    : "已安装，但当前没有格式在用（一般是备用组件）"));
                sb.AppendLine();
                sb.Append("应用时会按目标格式自动选用该软件对应的组件，无需纠结具体 CLSID。");
                return sb.ToString();
            }

            static string ShortExts(List<string> all, int maxLen)
            {
                var sb = new StringBuilder();
                foreach (string e in all)
                {
                    if (sb.Length + e.Length + 1 > maxLen) { sb.Append(" …"); break; }
                    sb.Append(" " + e);
                }
                return sb.ToString().Trim();
            }

            // 格式家族：比"类别"更细，只有真正处理同一家族格式的组件才算对口
            public static string Family(string ext)
            {
                if (ext == null) return "other";
                string e = ext.TrimStart('.').ToLowerInvariant();
                // 常见位图家族：jpg/png/bmp/gif/ico
                if (new[] { "jpg","jpeg","png","bmp","gif","ico","jfif" }.Contains(e)) return "common-image";
                // tif
                if (new[] { "tif","tiff" }.Contains(e)) return "tif";
                // webp
                if (e == "webp") return "webp";
                // heic/avif
                if (new[] { "heic","heif","hif","avif","avifs" }.Contains(e)) return "heic";
                // 相机 RAW
                if (new[] { "raw","arw","cr2","crw","nef","nrw","dng","pef","raf","orf","rw2","srw","x3f","kdc","mef","mos","mrw","ptx","erf","3fr","dcr","fff","iiq","sr2","srf","rwl","mfw" }.Contains(e)) return "raw";
                // 其他位图
                if (new[] { "qoi","tga","dds","eps","svgz" }.Contains(e)) return "other-image";
                // 矢量
                if (e == "svg") return "vector";
                // 3D
                if (e == "stl" || e == "3mf" || e == "obj") return "3d";
                // 代码
                if (e == "gcode" || e == "bgcode") return "code";
                // 文档
                if (e == "pdf" || e == "docx" || e == "xlsx" || e == "pptx" || e == "doc" || e == "xls" || e == "ppt" || e == "accdt" || e == "vsdx") return "doc";
                // 视频
                if (new[] { "mp4","mkv","avi","wmv","mov","m4v","webm","flv","ts","m2ts","mts","3gp","3g2" }.Contains(e)) return "video";
                // 音频
                if (new[] { "mp3","flac","wav","aac","ogg","wma","m4a","ape" }.Contains(e)) return "audio";
                return "other";
            }

            // 选 CLSID 的结果
            public class ClsidPick
            {
                public string Clsid;
                public string Quality; // exact / same / fallback
                public int SameCount;
            }

            public ClsidPick ClsidFor(string ext)
            {
                string fam = Family(ext);
                // 1) 机器注册里这个软件本来就服务该扩展名的组件 —— 最对口
                string mach = MachineHandler(ext);
                foreach (var it in Items)
                    if (mach != null && string.Equals(it.Clsid, mach, StringComparison.OrdinalIgnoreCase))
                        return new ClsidPick { Clsid = it.Clsid, Quality = "exact" };
                // 2) 同家族里覆盖格式最多的组件
                ClsidPick best = null;
                foreach (var it in Items)
                {
                    int same = 0;
                    foreach (string ue in it.UsedExts) if (Family(ue) == fam) same++;
                    if (same > 0 && (best == null || same > best.SameCount))
                        best = new ClsidPick { Clsid = it.Clsid, Quality = "same", SameCount = same };
                }
                if (best != null) return best;
                // 3) 兜底：选覆盖最多的组件（这个软件根本不处理这类格式，弹警告）
                string fallback = null; int bestN = -1;
                foreach (var it in Items)
                    if (it.UsedBy > bestN) { fallback = it.Clsid; bestN = it.UsedBy; }
                return new ClsidPick { Clsid = fallback ?? (Items.Count > 0 ? Items[0].Clsid : null), Quality = "fallback" };
            }
        }

        public static Provider Describe(string clsid, Dictionary<string, Provider> cache)
        {
            if (clsid == null || clsid.Trim().Length == 0) return null;
            clsid = clsid.Trim();
            if (cache.ContainsKey(clsid)) return cache[clsid];

            var p = new Provider();
            p.Clsid = clsid;
            p.Name = clsid;
            p.Registered = false;
            p.DllExists = false;

            string[] roots = new string[] { @"CLSID\" + clsid, @"CLSID\" + clsid, @"WOW6432Node\CLSID\" + clsid };
            RegistryKey[] hives = new RegistryKey[] { Registry.ClassesRoot, Registry.LocalMachine, Registry.LocalMachine };
            string[] subkeys = new string[] { null, @"SOFTWARE\Classes\CLSID\" + clsid, @"SOFTWARE\Classes\WOW6432Node\CLSID\" + clsid };

            for (int i = 0; i < subkeys.Length; i++)
            {
                RegistryKey root = null;
                try
                {
                    if (i == 0) root = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid);
                    else root = Registry.LocalMachine.OpenSubKey(subkeys[i]);
                    if (root == null) continue;

                    p.Registered = true;
                    object nm = root.GetValue(null);
                    if (nm != null && nm.ToString().Trim().Length > 0) p.Name = nm.ToString().Trim();

                    using (RegistryKey ips = root.OpenSubKey("InProcServer32"))
                    {
                        if (ips != null)
                        {
                            object dll = ips.GetValue(null);
                            if (dll != null)
                            {
                                string path = Environment.ExpandEnvironmentVariables(dll.ToString().Trim().Trim('"'));
                                if (path.Length > 0)
                                {
                                    p.Dll = path;
                                    p.DllExists = File.Exists(path);
                                }
                            }
                        }
                    }
                    root.Close();
                    break;
                }
                catch { if (root != null) root.Close(); }
            }

            if (!p.Registered && clsid == BuiltInClsid)
            {
                p.Name = "系统内置 照片缩略图";
                p.Dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\PhotoMetadataHandler.dll");
                p.DllExists = File.Exists(p.Dll);
            }
            else if (p.Dll != null && p.Name == clsid)
            {
                p.Name = Path.GetFileNameWithoutExtension(p.Dll);
            }

            // 读取 DLL 的产品名，把“Thumbnail Handler Class”这类匿名组件还原成真正的软件名
            if (p.Dll != null && p.DllExists)
            {
                try
                {
                    var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p.Dll);
                    string prod = vi.ProductName;
                    if (string.IsNullOrWhiteSpace(prod)) prod = vi.FileDescription;
                    if (!string.IsNullOrWhiteSpace(prod)) p.ProductName = prod.Trim();
                }
                catch { }
            }
            if (p.ProductName == null) p.ProductName = p.Name;
            cache[clsid] = p;
            return p;
        }
    }

    // ---------- Shell 缩略图探测（真实验证） ---------------------
    static class Probe
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        public static Image Get(string path, int size, out string info)
        {
            info = "";
            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            IShellItemImageFactory f = null;
            try
            {
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out f);
                if (hr != 0 || f == null) { info = "无法打开该文件 (0x" + hr.ToString("X8") + ")"; return null; }

                IntPtr hbm = IntPtr.Zero;
                f.GetImage(new SIZE(size, size), 0x8 /*SIIGBF_THUMBNAILONLY*/, out hbm); // 只接受真缩略图
                if (hbm == IntPtr.Zero) { info = "没有返回图像"; return null; }

                Image img = Image.FromHbitmap(hbm);
                DeleteObject(hbm);
                info = "缩略图尺寸 " + img.Width + " × " + img.Height;
                return img;
            }
            catch (Exception ex)
            {
                int hr = Marshal.GetHRForException(ex);
                info = "生成失败 (0x" + hr.ToString("X8") + ")：该格式的缩略图引擎不可用";
                return null;
            }
            finally
            {
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }
    }

    // ---------- 主窗体 -------------------------------------------
    public class MainForm : Form
    {
        ListView list;
        ComboBox combo;
        Label providerDetail;
        PictureBox preview;
        Label testResult;
        StatusStrip status;
        ToolStripStatusLabel statusLabel;
        Button btnApplySel, btnApplyImages, btnApplyAll, btnTest, btnClearCache, btnRescan, btnResetAll, btnOpenBackup;

        List<string> formats = new List<string>();
        Dictionary<string, string> machineMap;
        Dictionary<string, Thumbs.Provider> providerCache = new Dictionary<string, Thumbs.Provider>(StringComparer.OrdinalIgnoreCase);
        List<Thumbs.Provider> providers = new List<Thumbs.Provider>();
        List<Thumbs.Product> products = new List<Thumbs.Product>();

        static readonly Color OkColor = Color.FromArgb(22, 130, 70);
        static readonly Color BadColor = Color.FromArgb(190, 40, 40);
        static readonly Color WarnColor = Color.FromArgb(170, 110, 0);

        public MainForm()
        {
            Text = "缩略图引擎选择器";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(1060, 700);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;

            // 顶部标题区
            Panel header = new Panel { Dock = DockStyle.Top, Height = 120, BackColor = Color.FromArgb(248, 249, 251), Padding = new Padding(18, 10, 18, 8) };
            var title = new Label { Text = "缩略图引擎选择器", Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), AutoSize = true, Location = new Point(18, 12) };
            var sub = new Label
            {
                Text = "为每种文件格式选择缩略图引擎。下拉框同时列出系统内置与第三方看图软件的组件，并标注是否可用。",
                AutoSize = true, MaximumSize = new Size(1020, 0), ForeColor = Color.FromArgb(90, 96, 105), Location = new Point(20, 42)
            };
            header.Controls.Add(title);
            header.Controls.Add(sub);

            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(18, 4, 0, 4) };
            btnRescan = MakeButton("重新扫描", 86);
            btnResetAll = MakeButton("全部改回系统内置", 132);
            btnClearCache = MakeButton("清理缓存并重启资源管理器", 176);
            btnOpenBackup = MakeButton("打开备份目录", 110);
            tools.Controls.Add(btnRescan);
            tools.Controls.Add(btnResetAll);
            tools.Controls.Add(btnClearCache);
            tools.Controls.Add(btnOpenBackup);
            header.Controls.Add(tools);
            Controls.Add(header);

            // 底部状态栏
            status = new StatusStrip { SizingGrip = false };
            statusLabel = new ToolStripStatusLabel("准备就绪");
            status.Items.Add(statusLabel);
            Controls.Add(status);

            // 主内容：右侧固定宽度的工具栏 + 左侧填充列表
            Panel right = new Panel { Dock = DockStyle.Right, Width = 430, Padding = new Padding(14, 10, 14, 10), BackColor = Color.FromArgb(248, 249, 251) };
            Controls.Add(right);

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            list.Columns.Add("格式", 86);
            list.Columns.Add("当前的缩略图引擎", 330);
            list.Columns.Add("状态", 96);
            list.Columns.Add("来源", 92);
            list.SelectedIndexChanged += delegate { SyncComboSelection(); };
            list.Padding = new Padding(12, 10, 0, 10);
            Controls.Add(list);

            // 右侧面板内容
            var lbl1 = new Label { Text = "选择由哪个软件生成缩略图", Dock = DockStyle.Top, Height = 28, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 30 };
            combo.SelectedIndexChanged += delegate { ShowProviderDetail(); };
            providerDetail = new Label { Dock = DockStyle.Top, Height = 178, Padding = new Padding(2, 8, 2, 0), ForeColor = Color.FromArgb(80, 86, 94) };
            btnApplySel = MakeButtonWide("应用到选中的格式");
            btnApplyImages = MakeButtonWide("应用到所有常见图片格式");
            btnApplyAll = MakeButtonWide("应用到全部列出的格式");

            var grpTest = new GroupBox { Dock = DockStyle.Top, Height = 250, Text = " 实测预览 ", Padding = new Padding(10, 6, 10, 8) };
            btnTest = MakeButtonWide("选择一个文件测试缩略图");
            preview = new PictureBox { Dock = DockStyle.Top, Height = 150, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
            testResult = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.FromArgb(80, 86, 94) };
            grpTest.Controls.Add(testResult);
            grpTest.Controls.Add(preview);
            grpTest.Controls.Add(btnTest);

            // Dock 顺序：后加入的先停靠在上方
            right.Controls.Add(grpTest);
            right.Controls.Add(btnApplyAll);
            right.Controls.Add(btnApplyImages);
            right.Controls.Add(btnApplySel);
            right.Controls.Add(providerDetail);
            right.Controls.Add(combo);
            right.Controls.Add(lbl1);

            btnRescan.Click += delegate { Reload(true); };
            btnResetAll.Click += ResetAll_Click;
            btnClearCache.Click += ClearCache_Click;
            btnOpenBackup.Click += delegate { OpenBackupFolder(); };
            btnApplySel.Click += delegate { ApplyToSelection(); };
            btnApplyImages.Click += delegate { ApplyToFormats(Thumbs.ImageFormats, true); };
            btnApplyAll.Click += delegate { ApplyToFormats(new List<string>(formats).ToArray(), false); };
            btnTest.Click += Test_Click;

            Reload(false);

            // 让填充式列表最后参与停靠，避免盖住标题栏和右侧面板
            Controls.SetChildIndex(list, 0);
        }

        static Button MakeButton(string text, int width)
        {
            var b = new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(0, 0, 10, 0) };
            return b;
        }

        static Button MakeButtonWide(string text)
        {
            var b = new Button { Text = text, Dock = DockStyle.Top, Height = 34, FlatStyle = FlatStyle.System, Margin = new Padding(0, 4, 0, 4) };
            return b;
        }

        // ---- 数据加载 -------------------------------------------
        public void Reload(bool rescan)
        {
            formats = Thumbs.AllFormats(out machineMap);
            providerCache = new Dictionary<string, Thumbs.Provider>(StringComparer.OrdinalIgnoreCase);

            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var samples = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allExts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var distinct = new List<string>();
            foreach (string ext in formats)
            {
                string h = Thumbs.Effective(ext);
                if (h == null) continue;
                if (!used.ContainsKey(h)) { used[h] = 0; distinct.Add(h); samples[h] = new List<string>(); allExts[h] = new List<string>(); }
                used[h]++;
                allExts[h].Add(ext);
                if (samples[h].Count < 3) samples[h].Add(ext);
            }
            if (!used.ContainsKey(Thumbs.BuiltInClsid)) { used[Thumbs.BuiltInClsid] = 0; samples[Thumbs.BuiltInClsid] = new List<string>(); allExts[Thumbs.BuiltInClsid] = new List<string>(); }

            // 补上系统中已安装、但当前没有任何格式在用的缩略图组件
            List<string> installed = Thumbs.FindInstalledThumbProviders();
            foreach (string c in installed)
            {
                if (used.ContainsKey(c)) continue;
                var probe = Thumbs.Describe(c, providerCache);
                if (probe != null && probe.Registered && probe.DllExists)
                {
                    used[c] = 0;
                    samples[c] = new List<string>();
                    distinct.Add(c);
                }
            }
            if (!distinct.Contains(Thumbs.BuiltInClsid)) distinct.Add(Thumbs.BuiltInClsid);

            providers = new List<Thumbs.Provider>();
            foreach (string c in distinct)
            {
                var p = Thumbs.Describe(c, providerCache);
                if (p == null) continue;
                p.UsedBy = used.ContainsKey(c) ? used[c] : 0;
                var s = samples.ContainsKey(c) ? samples[c] : new List<string>();
                p.SampleExts = s.Count > 0 ? string.Join(" ", s.ToArray()) : "";
                p.UsedExts = allExts.ContainsKey(c) ? allExts[c] : new List<string>();
                providers.Add(p);
            }

            // 按软件（DLL）合并：同一个看图软件的多个 CLSID 只显示一行
            products = new List<Thumbs.Product>();
            var byKey = new Dictionary<string, Thumbs.Product>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in providers)
            {
                string key = string.IsNullOrEmpty(p.Dll) ? ("<nohost>" + p.Clsid) : p.Dll;
                if (!byKey.ContainsKey(key))
                {
                    var pr = new Thumbs.Product();
                    pr.Dll = p.Dll;
                    pr.Name = Thumbs.FriendlyName(p.Dll, p.ProductName ?? p.Name);
                    byKey[key] = pr;
                    products.Add(pr);
                }
                var prod = byKey[key];
                prod.Items.Add(p);
                if (!(p.Registered && p.DllExists)) prod.Ok = false;
            }
            // 统计每个软件覆盖了哪些格式
            foreach (string ext in formats)
            {
                string h = Thumbs.Effective(ext);
                if (h == null) continue;
                var p = providerCache.ContainsKey(h) ? providerCache[h] : null;
                if (p == null || string.IsNullOrEmpty(p.Dll)) continue;
                Thumbs.Product prod;
                if (byKey.TryGetValue(p.Dll, out prod)) prod.Exts.Add(ext);
            }
            // 显示文字：软件名 + 覆盖格式数 + 前几个格式
            foreach (var prod in products)
            {
                string dll = prod.Dll == null ? "未知组件" : Path.GetFileName(prod.Dll);
                if (prod.Exts.Count > 0)
                {
                    var exSample = prod.Exts.GetRange(0, Math.Min(4, prod.Exts.Count));
                    prod.Display = prod.Name + "（" + dll + "）— 覆盖 " + prod.Exts.Count + " 个格式 " + string.Join(" ", exSample.ToArray()) + (prod.Exts.Count > 4 ? " …" : "");
                }
                else
                {
                    prod.Display = prod.Name + "（" + dll + "）— 已安装，当前无格式在用";
                }
            }
            products.Sort(delegate (Thumbs.Product a, Thumbs.Product b)
            {
                if (a.Exts.Count != b.Exts.Count) return b.Exts.Count - a.Exts.Count;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });

            combo.Items.Clear();
            foreach (var prod in products) combo.Items.Add(prod);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;

            FillList();
            int inUseProducts = 0;
            foreach (var prod in products) if (prod.Exts.Count > 0) inUseProducts++;
            statusLabel.Text = "共 " + formats.Count + " 种格式 · " + products.Count + " 个软件（" + inUseProducts + " 个正在使用）" + (rescan ? "　已重新扫描" : "");
        }

        void FillList()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (string ext in formats)
            {
                string h = Thumbs.Effective(ext);
                var item = new ListViewItem(ext.ToUpperInvariant());
                var p = h == null ? null : Thumbs.Describe(h, providerCache);

                string pname = "（未注册缩略图引擎）";
                string state = "缺失";
                Color color = BadColor;
                if (p != null)
                {
                    pname = Thumbs.FriendlyName(p.Dll, p.ProductName ?? p.Name) + (p.Dll == null ? "" : "  ·  " + Path.GetFileName(p.Dll));
                    if (!p.Registered) { state = "未注册"; color = BadColor; }
                    else if (!p.DllExists) { state = "组件缺失"; color = BadColor; }
                    else { state = "正常"; color = OkColor; }
                }
                item.SubItems.Add(pname);
                item.SubItems.Add(state);
                string uo = Thumbs.UserOverride(ext);
                item.SubItems.Add(uo != null ? "用户级(优先)" : (p != null ? "系统注册" : "无"));
                if (color != OkColor) item.ForeColor = color;
                item.Tag = h;
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        void ShowProviderDetail()
        {
            var prod = combo.SelectedItem as Thumbs.Product;
            if (prod == null) { providerDetail.Text = ""; return; }
            providerDetail.Text = prod.Detail();
            providerDetail.ForeColor = prod.Ok ? Color.FromArgb(80, 86, 94) : BadColor;
        }

        void SyncComboSelection()
        {
            if (list.SelectedItems.Count != 1) return;
            string h = list.SelectedItems[0].Tag as string;
            if (h == null) return;
            Thumbs.Provider p;
            if (!providerCache.TryGetValue(h, out p) || p == null) return;
            string dll = p.Dll;
            if (dll == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var prod = combo.Items[i] as Thumbs.Product;
                if (prod != null && string.Equals(prod.Dll, dll, StringComparison.OrdinalIgnoreCase)) { if (combo.SelectedIndex != i) combo.SelectedIndex = i; return; }
            }
        }

        // ---- 应用 -----------------------------------------------
        void ApplyToSelection()
        {
            if (list.SelectedItems.Count == 0) { MessageBox.Show(this, "请先在左侧选择要修改的格式（可按住 Ctrl 多选）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var exts = new List<string>();
            foreach (ListViewItem it in list.SelectedItems) exts.Add(it.Text.ToLowerInvariant());
            ApplyToFormats(exts.ToArray(), true);
        }

        void ApplyToFormats(string[] exts, bool confirm)
        {
            var prod = combo.SelectedItem as Thumbs.Product;
            if (prod == null || prod.Items.Count == 0) return;
            if (!prod.Ok)
            {
                var r = MessageBox.Show(this,
                    "所选软件的组件有缺失，写入后部分格式可能仍无法显示缩略图。\n\n仍然继续吗？",
                    "组件不完整", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            if (confirm)
            {
                var r = MessageBox.Show(this, "将把【" + prod.Name + "】指定给 " + exts.Length + " 种格式：\n" + string.Join("  ", exts) + "\n\n继续？",
                    "确认修改", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            Backup(exts);
            int ok = 0, fail = 0;
            var fallbacks = new List<string>();
            foreach (string e in exts)
            {
                string ext = e.StartsWith(".") ? e : "." + e;
                var pick = prod.ClsidFor(ext);
                if (pick.Quality == "fallback") fallbacks.Add(ext);
                if (pick.Clsid != null && Thumbs.SetHandler(ext, pick.Clsid)) ok++; else fail++;
            }
            if (fallbacks.Count > 0)
            {
                MessageBox.Show(this,
                    "注意：【" + prod.Name + "】没有专门处理以下格式的组件，\n" +
                    "程序只能用它最通用的组件勉强兜底，缩略图可能不正确或显示成占位图：\n\n" +
                    string.Join(" ", fallbacks.ToArray()) +
                    "\n\n建议：这类格式改用“Windows 系统（照片）”，或重装对应看图软件。",
                    "部分格式可能不对口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Reload(false);
            statusLabel.Text = "已把 " + prod.Name + " 应用到 " + ok + " 种格式" + (fail > 0 ? "（" + fail + " 项失败）" : "");
            if (ok > 0) AskRefreshCache();
        }

        void ResetAll_Click(object sender, EventArgs e)
        {
            var r = MessageBox.Show(this, "将清除本工具为所有格式添加的“用户级”覆盖，恢复系统原本注册的引擎。\n（本工具修改前都会自动备份，可随时找回。）\n\n继续？",
                "全部改回系统默认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            int n = 0;
            if (formats.Count > 0) Backup(new string[0]);
            foreach (string ext in formats)
            {
                if (Thumbs.UserOverride(ext) != null) { Thumbs.ClearOverride(ext); n++; }
            }
            Reload(false);
            statusLabel.Text = "已清除 " + n + " 项本工具写入的覆盖";
            if (n > 0) AskRefreshCache();
        }

        void ClearCache_Click(object sender, EventArgs e) { DoClearCache(true); }

        void AskRefreshCache()
        {
            var r = MessageBox.Show(this, "设置已写入。需要清空旧的缩略图缓存并重启资源管理器才能立刻看到效果。\n\n重启资源管理器会关闭当前所有文件窗口（不会影响正在运行的其他程序）。现在执行吗？",
                "让修改生效", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) DoClearCache(false);
        }

        void DoClearCache(bool ask)
        {
            if (ask)
            {
                var r = MessageBox.Show(this, "将关闭并重启资源管理器，并删除缩略图缓存文件。\n已打开的文件窗口会被关闭。继续？",
                    "清理缓存并重启资源管理器", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            Cursor = Cursors.WaitCursor;
            statusLabel.Text = "正在重启资源管理器…";
            Application.DoEvents();

            int removed = 0;
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/f /im explorer.exe") { CreateNoWindow = true, UseShellExecute = false };
                using (var pr = Process.Start(psi)) { pr.WaitForExit(5000); }
                System.Threading.Thread.Sleep(1500);

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "thumbcache_*.db"))
                    {
                        try { File.Delete(f); removed++; } catch { }
                    }
                }
            }
            catch (Exception ex) { statusLabel.Text = "清理出错：" + ex.Message; }

            try
            {
                string exp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(exp) { UseShellExecute = true });
            }
            catch { }

            Cursor = Cursors.Default;
            statusLabel.Text = "已删除 " + removed + " 个缓存文件，资源管理器已重启";
        }

        // ---- 备份 -----------------------------------------------
        public static string BackupDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "缩略图引擎选择器\\备份");
            Directory.CreateDirectory(dir);
            return dir;
        }

        void Backup(string[] exts)
        {
            try
            {
                string file = Path.Combine(BackupDir(), "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("# 缩略图引擎选择器 自动备份  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("# 格式\t修改前的有效引擎(CLSID)\t系统注册值\t用户覆盖值");
                string[] targets = (exts != null && exts.Length > 0) ? exts : new List<string>(formats).ToArray();
                foreach (string e in targets)
                {
                    string ext = e.StartsWith(".") ? e : "." + e;
                    sb.AppendLine(ext + "\t" + (Thumbs.Effective(ext) ?? "") + "\t" + (Thumbs.MachineHandler(ext) ?? "") + "\t" + (Thumbs.UserOverride(ext) ?? ""));
                }
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void OpenBackupFolder()
        {
            try { Process.Start(new ProcessStartInfo(BackupDir()) { UseShellExecute = true }); } catch { }
        }

        // ---- 实测 -----------------------------------------------
        void Test_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择一个文件来测试它的缩略图";
                dlg.Filter = "常见格式|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.pdf;*.docx;*.xlsx;*.pptx;*.mp4|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var old = preview.Image;
                string info;
                Image img = Probe.Get(dlg.FileName, 320, out info);
                preview.Image = img;
                if (old != null) old.Dispose();
                testResult.Text = info + "\n文件：" + Path.GetFileName(dlg.FileName);
                testResult.ForeColor = img != null ? OkColor : BadColor;
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 1 && args[0] == "--selftest")
            {
                string logPath = args.Length >= 2 ? args[1] : Path.Combine(Path.GetTempPath(), "thumbpicker-selftest.txt");
                SelfTest(logPath);
                return;
            }
            if (args != null && args.Length >= 1 && args[0] == "--smoke")
            {
                string logPath = args.Length >= 2 ? args[1] : Path.Combine(Path.GetTempPath(), "thumbpicker-smoke.txt");
                Smoke(logPath);
                return;
            }
            if (args != null && args.Length >= 2 && args[0] == "--shot")
            {
                Shot(args[1]);
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { LogCrash(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { LogCrash(e.ExceptionObject as Exception); };
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show("程序出错，详细信息已写入日志：\n" + LogPath() + "\n\n" + ex.Message, "缩略图引擎选择器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static string LogPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "缩略图引擎选择器");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "crash.log");
        }

        static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(LogPath(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + (ex == null ? "(null)" : ex.ToString()) + "\r\n\r\n", Encoding.UTF8); }
            catch { }
        }

        // 把界面渲染成图片，便于在无桌面环境下检查排版
        static void Shot(string outPath)
        {
            try
            {
                var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-3000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
            }
            catch (Exception ex) { File.WriteAllText(outPath + ".error.txt", ex.ToString(), Encoding.UTF8); }
        }

        // 构造窗体但不显示，用来检查界面初始化是否出错
        static void Smoke(string logPath)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("构造窗体…");
                var f = new MainForm();
                sb.AppendLine("窗体构造成功，文本=" + f.Text + " 尺寸=" + f.ClientSize);
                sb.AppendLine("顶层控件数=" + f.Controls.Count);
                f.Dispose();
                sb.AppendLine("SMOKE_OK");
            }
            catch (Exception ex) { sb.AppendLine("SMOKE_ERROR: " + ex); }
            File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
        }

        // 无界面自检：枚举格式、列出引擎、真实探测缩略图、验证写入/回滚
        static void SelfTest(string logPath)
        {
            var sb = new StringBuilder();
            try
            {
                Dictionary<string, string> mm;
                List<string> fmts = Thumbs.AllFormats(out mm);
                sb.AppendLine("[1] 枚举格式数量: " + fmts.Count);

                var cache = new Dictionary<string, Thumbs.Provider>(StringComparer.OrdinalIgnoreCase);
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string ext in fmts)
                {
                    string h = Thumbs.Effective(ext);
                    if (h == null) continue;
                    seen[h] = (seen.ContainsKey(h) ? seen[h] : 0) + 1;
                }
                sb.AppendLine("[2] 不同引擎数量: " + seen.Count);
                foreach (var kv in seen)
                {
                    var p = Thumbs.Describe(kv.Key, cache);
                    sb.AppendLine("    " + (p.DllExists && p.Registered ? "可用  " : "失效  ") + p.Name + " | " + kv.Value + " 格式 | " + p.Clsid + " | " + p.Dll);
                }

                string sample = null;
                foreach (string f in Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), "*.jpg"))
                {
                    sample = f; break;
                }
                if (sample != null)
                {
                    string info;
                    Image img = Probe.Get(sample, 256, out info);
                    sb.AppendLine("[3] 探测 " + Path.GetFileName(sample) + " -> " + info);
                    if (img != null) { sb.AppendLine("    位图大小: " + img.Width + "x" + img.Height); img.Dispose(); }
                }
                else sb.AppendLine("[3] 桌面上没有 jpg 样本，跳过探测");

                // 写入/回滚测试（用一个临时扩展名，不碰用户已有的格式）
                string testExt = ".thumbselftest";
                bool w = Thumbs.SetHandler(testExt, Thumbs.BuiltInClsid);
                string after = Thumbs.Effective(testExt);
                bool c = Thumbs.ClearOverride(testExt);
                string cleared = Thumbs.Effective(testExt);
                sb.AppendLine("[4] 写入=" + w + " 生效值=" + after + " 清除=" + c + " 清除后=" + (cleared ?? "(空)"));
                sb.AppendLine("[5] 备份目录: " + MainForm.BackupDir());
                sb.AppendLine("SELFTEST_DONE");
            }
            catch (Exception ex)
            {
                sb.AppendLine("SELFTEST_ERROR: " + ex);
            }
            File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
