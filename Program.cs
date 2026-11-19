using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RicohPrinterSetup
{
    public class PrinterConfig
    {
        public string id { get; set; }
        public string displayName { get; set; }
        public string modelName { get; set; }
        public string ip { get; set; }
        public string infRelativePath { get; set; }
        public string location { get; set; }

        // Runtime states
        [ScriptIgnore]
        public bool IsInstalled { get; set; }
        [ScriptIgnore]
        public bool IsOnline { get; set; }
        [ScriptIgnore]
        public long PingMs { get; set; }
    }

    public class ModelProfile
    {
        public string Display { get; set; }
        public string DriverName { get; set; }
        public string InfPath { get; set; }
        public string Prefix { get; set; }

        public override string ToString()
        {
            return Display;
        }

        public static List<ModelProfile> LoadFromDriversFolder(string baseDir)
        {
            Dictionary<string, ModelProfile> profileMap = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);
            string driversDir = Path.Combine(baseDir, "drivers");

            if (Directory.Exists(driversDir))
            {
                try
                {
                    string[] infFiles = Directory.GetFiles(driversDir, "*.inf", SearchOption.AllDirectories);
                    foreach (string inf in infFiles)
                    {
                        try
                        {
                            string relPath = inf.Substring(baseDir.TrimEnd('\\', '/').Length + 1);
                            string[] lines = File.ReadAllLines(inf);
                            foreach (string line in lines)
                            {
                                Match m = Regex.Match(line, "^\"(RICOH [^\"]+PCL 6)\"=", RegexOptions.IgnoreCase);
                                if (!m.Success)
                                {
                                    m = Regex.Match(line, "DrvName\\w*=\"(RICOH [^\"]+PCL 6)\"", RegexOptions.IgnoreCase);
                                }
                                if (m.Success)
                                {
                                    string fullName = m.Groups[1].Value.Trim();
                                    if (!profileMap.ContainsKey(fullName))
                                    {
                                        string prefix = fullName.Replace(" PCL 6", "").Replace("RICOH ", "Ricoh ");
                                        profileMap[fullName] = new ModelProfile
                                        {
                                            Display = fullName,
                                            DriverName = fullName,
                                            InfPath = relPath,
                                            Prefix = prefix
                                        };
                                    }
                                }
                            }
                        }
                        catch {}
                    }
                }
                catch {}
            }

            List<ModelProfile> list = new List<ModelProfile>(profileMap.Values);
            list.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));

            if (list.Count == 0)
            {
                return GetDefaults();
            }

            return list;
        }

        public static List<ModelProfile> GetDefaults()
        {
            return new List<ModelProfile>
            {
                new ModelProfile {
                    Display = "RICOH MP C3503 PCL 6",
                    DriverName = "RICOH MP C3503 PCL 6",
                    InfPath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    Prefix = "Ricoh MP C3503"
                },
                new ModelProfile {
                    Display = "RICOH IM C2000 PCL 6",
                    DriverName = "RICOH IM C2000 PCL 6",
                    InfPath = @"drivers\IM C2000-C2500\disk1\MPC2000_.inf",
                    Prefix = "Ricoh IM C2000"
                },
                new ModelProfile {
                    Display = "RICOH Aficio MP C2800 PCL 6",
                    DriverName = "RICOH Aficio MP C2800 PCL 6",
                    InfPath = @"drivers\MP C2800-C3300\disk1\OEMSETUP.INF",
                    Prefix = "Ricoh Aficio MP C2800"
                }
            };
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private List<PrinterConfig> printers = new List<PrinterConfig>();
        private FlowLayoutPanel flowPrinters;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnRefresh;
        private Button btnScan;
        private Button btnManualAdd;
        private Button btnToggleLog;
        private Panel panelLog;
        private Panel bottomContainer;
        private string configPath;

        public MainForm()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printers.json");
            InitializeComponents();
            LoadConfiguration();
            RefreshAllStatusAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "Ricoh Office Printer Setup";
            this.Size = new Size(1060, 760);
            this.MinimumSize = new Size(950, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Top Header Panel
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 90;
            headerPanel.BackColor = Color.FromArgb(26, 32, 44);

            Panel redBar = new Panel();
            redBar.Dock = DockStyle.Top;
            redBar.Height = 4;
            redBar.BackColor = Color.FromArgb(207, 16, 45); // Ricoh Red
            headerPanel.Controls.Add(redBar);

            // Left Title Info container
            Panel titlePanel = new Panel();
            titlePanel.Dock = DockStyle.Left;
            titlePanel.Width = 420;
            titlePanel.BackColor = Color.Transparent;

            Label lblTitle = new Label();
            lblTitle.Text = "RICOH Office Printer Setup";
            lblTitle.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(22, 18);
            lblTitle.AutoSize = true;
            titlePanel.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Self-Service 1-Click Printer Setup for Workstations";
            lblSubtitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(148, 163, 184);
            lblSubtitle.Location = new Point(23, 50);
            lblSubtitle.AutoSize = true;
            titlePanel.Controls.Add(lblSubtitle);

            headerPanel.Controls.Add(titlePanel);

            // Top Right Action Buttons
            FlowLayoutPanel headerButtonsPanel = new FlowLayoutPanel();
            headerButtonsPanel.Dock = DockStyle.Right;
            headerButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
            headerButtonsPanel.WrapContents = false;
            headerButtonsPanel.AutoSize = true;
            headerButtonsPanel.Padding = new Padding(0, 24, 20, 0);
            headerButtonsPanel.BackColor = Color.Transparent;

            btnManualAdd = CreateFlatButton("➕ Add Manually (Offline)", Color.FromArgb(16, 185, 129), Color.White, 195, 38);
            btnManualAdd.Margin = new Padding(0, 0, 10, 0);
            btnManualAdd.Click += (s, e) => OpenManualAddDialog();
            headerButtonsPanel.Controls.Add(btnManualAdd);

            btnScan = CreateFlatButton("🔍 Scan Network", Color.FromArgb(207, 16, 45), Color.White, 155, 38);
            btnScan.Margin = new Padding(0, 0, 10, 0);
            btnScan.Click += (s, e) => OpenScannerDialog();
            headerButtonsPanel.Controls.Add(btnScan);

            btnRefresh = CreateFlatButton("🔄 Refresh", Color.FromArgb(51, 65, 85), Color.White, 115, 38);
            btnRefresh.Click += (s, e) => {
                LoadConfiguration();
                RefreshAllStatusAsync();
            };
            headerButtonsPanel.Controls.Add(btnRefresh);

            headerPanel.Controls.Add(headerButtonsPanel);
            this.Controls.Add(headerPanel);

            // Bottom Status & Log Panel
            bottomContainer = new Panel();
            bottomContainer.Dock = DockStyle.Bottom;
            bottomContainer.Height = 44;
            bottomContainer.BackColor = Color.White;

            Panel statusStrip = new Panel();
            statusStrip.Dock = DockStyle.Top;
            statusStrip.Height = 44;
            statusStrip.BackColor = Color.FromArgb(241, 245, 249);

            lblStatus = new Label();
            lblStatus.Text = "Ready";
            lblStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(51, 65, 85);
            lblStatus.Location = new Point(20, 12);
            lblStatus.AutoSize = true;
            statusStrip.Controls.Add(lblStatus);

            FlowLayoutPanel statusRightPanel = new FlowLayoutPanel();
            statusRightPanel.Dock = DockStyle.Right;
            statusRightPanel.FlowDirection = FlowDirection.RightToLeft;
            statusRightPanel.WrapContents = false;
            statusRightPanel.AutoSize = true;
            statusRightPanel.Padding = new Padding(0, 7, 20, 0);
            statusRightPanel.BackColor = Color.Transparent;

            btnToggleLog = CreateFlatButton("📋 Show Activity Log", Color.FromArgb(203, 213, 225), Color.FromArgb(30, 41, 59), 160, 28);
            btnToggleLog.Click += (s, e) => {
                bool isShowing = (bottomContainer.Height > 60);
                if (isShowing)
                {
                    bottomContainer.Height = 44;
                    btnToggleLog.Text = "📋 Show Activity Log";
                }
                else
                {
                    bottomContainer.Height = 180;
                    btnToggleLog.Text = "✖ Hide Activity Log";
                }
            };
            statusRightPanel.Controls.Add(btnToggleLog);

            progressBar = new ProgressBar();
            progressBar.Size = new Size(200, 18);
            progressBar.Margin = new Padding(0, 5, 12, 0);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Visible = false;
            statusRightPanel.Controls.Add(progressBar);

            statusStrip.Controls.Add(statusRightPanel);
            bottomContainer.Controls.Add(statusStrip);

            panelLog = new Panel();
            panelLog.Dock = DockStyle.Fill;
            panelLog.Padding = new Padding(12);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.FromArgb(15, 23, 42);
            txtLog.ForeColor = Color.FromArgb(226, 232, 240);
            txtLog.Font = new Font("Consolas", 9f);
            panelLog.Controls.Add(txtLog);

            bottomContainer.Controls.Add(panelLog);
            this.Controls.Add(bottomContainer);

            // Center Flow Panel for Printer Cards
            flowPrinters = new FlowLayoutPanel();
            flowPrinters.Dock = DockStyle.Fill;
            flowPrinters.AutoScroll = true;
            flowPrinters.FlowDirection = FlowDirection.TopDown;
            flowPrinters.WrapContents = false;
            flowPrinters.Padding = new Padding(20, 15, 20, 15);
            flowPrinters.BackColor = Color.FromArgb(248, 250, 252);
            this.Controls.Add(flowPrinters);
            flowPrinters.BringToFront();

            this.Resize += (s, e) => {
                UpdateCardWidths();
            };
        }

        private void UpdateCardWidths()
        {
            int cardWidth = flowPrinters.ClientSize.Width - 45;
            if (cardWidth < 850) cardWidth = 850;

            foreach (Control c in flowPrinters.Controls)
            {
                if (c is Panel)
                {
                    c.Width = cardWidth;
                }
            }
        }

        private Button CreateFlatButton(string text, Color bg, Color fg, int width, int height)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(width, height);
            btn.Cursor = Cursors.Hand;
            btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            return btn;
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    printers = js.Deserialize<List<PrinterConfig>>(json);
                    if (printers == null) printers = new List<PrinterConfig>();
                    Log("Loaded " + printers.Count + " printers from printers.json");
                }
                else
                {
                    CreateDefaultConfiguration();
                }
            }
            catch (Exception ex)
            {
                Log("Error reading printers.json: " + ex.Message + ". Falling back to defaults.");
                CreateDefaultConfiguration();
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(configPath, js.Serialize(printers));
            }
            catch (Exception ex)
            {
                Log("Error saving printers.json: " + ex.Message);
            }
        }

        public void CreateDefaultConfiguration()
        {
            printers = new List<PrinterConfig>
            {
                new PrinterConfig {
                    id = "c3503_160",
                    displayName = "Ricoh MP C3503 (160)",
                    modelName = "RICOH MP C3503 PCL 6",
                    ip = "192.168.23.160",
                    infRelativePath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    location = "Main Office (192.168.23.160)"
                },
                new PrinterConfig {
                    id = "c3503_177",
                    displayName = "Ricoh MP C3503 (177)",
                    modelName = "RICOH MP C3503 PCL 6",
                    ip = "192.168.23.177",
                    infRelativePath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    location = "Office Department (192.168.23.177)"
                },
                new PrinterConfig {
                    id = "c2800_152",
                    displayName = "Ricoh Aficio MP C2800",
                    modelName = "RICOH Aficio MP C2800 PCL 6",
                    ip = "192.168.23.152",
                    infRelativePath = @"drivers\MP C2800-C3300\disk1\OEMSETUP.INF",
                    location = "HR / Accounts (192.168.23.152)"
                },
                new PrinterConfig {
                    id = "c2000_168",
                    displayName = "Ricoh IM C2000",
                    modelName = "RICOH IM C2000 PCL 6",
                    ip = "192.168.23.168",
                    infRelativePath = @"drivers\IM C2000-C2500\disk1\MPC2000_.inf",
                    location = "Operations / Meeting (192.168.23.168)"
                }
            };

            SaveConfiguration();
        }

        public void RefreshAllStatusAsync()
        {
            btnRefresh.Enabled = false;
            lblStatus.Text = "Checking network and installed printer statuses...";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;

            Task.Factory.StartNew(() =>
            {
                HashSet<string> installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    installedNames.Add(p);
                }

                Parallel.ForEach(printers, printer =>
                {
                    printer.IsInstalled = installedNames.Contains(printer.displayName) ||
                                          installedNames.Contains(printer.modelName);

                    try
                    {
                        using (Ping p = new Ping())
                        {
                            PingReply reply = p.Send(printer.ip, 900);
                            if (reply.Status == IPStatus.Success)
                            {
                                printer.IsOnline = true;
                                printer.PingMs = reply.RoundtripTime;
                            }
                            else
                            {
                                printer.IsOnline = false;
                            }
                        }
                    }
                    catch
                    {
                        printer.IsOnline = false;
                    }
                });

                this.Invoke((Action)(() =>
                {
                    RenderPrinterCards();
                    progressBar.Visible = false;
                    btnRefresh.Enabled = true;
                    lblStatus.Text = "Ready - All printer statuses updated.";
                }));
            });
        }

        private void RenderPrinterCards()
        {
            flowPrinters.SuspendLayout();
            flowPrinters.Controls.Clear();

            int cardWidth = flowPrinters.ClientSize.Width - 45;
            if (cardWidth < 850) cardWidth = 850;

            if (printers.Count == 0)
            {
                Panel emptyCard = new Panel();
                emptyCard.Size = new Size(cardWidth, 160);
                emptyCard.BackColor = Color.White;
                emptyCard.Padding = new Padding(24);
                emptyCard.Paint += (s, e) =>
                {
                    ControlPaint.DrawBorder(e.Graphics, emptyCard.ClientRectangle,
                        Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
                };

                Label lblEmptyTitle = new Label();
                lblEmptyTitle.Text = "📭 No Printers Currently in List for this Client";
                lblEmptyTitle.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
                lblEmptyTitle.ForeColor = Color.FromArgb(51, 65, 85);
                lblEmptyTitle.Location = new Point(24, 20);
                lblEmptyTitle.AutoSize = true;
                emptyCard.Controls.Add(lblEmptyTitle);

                Label lblEmptyDesc = new Label();
                lblEmptyDesc.Text = "All previous client printers have been cleared. You can add new printers by scanning the client's network,\npre-configuring an offline machine, or restoring the office default printers below.";
                lblEmptyDesc.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
                lblEmptyDesc.ForeColor = Color.FromArgb(100, 116, 139);
                lblEmptyDesc.Location = new Point(25, 52);
                lblEmptyDesc.AutoSize = true;
                emptyCard.Controls.Add(lblEmptyDesc);

                Button btnRestoreDefaults = CreateFlatButton("🔄 Restore Office Default Printers", Color.FromArgb(51, 65, 85), Color.White, 240, 36);
                btnRestoreDefaults.Location = new Point(26, 105);
                btnRestoreDefaults.Click += (s, e) => {
                    CreateDefaultConfiguration();
                    RefreshAllStatusAsync();
                };
                emptyCard.Controls.Add(btnRestoreDefaults);

                Button btnAddNow = CreateFlatButton("➕ Add Printer Manually (Offline)", Color.FromArgb(16, 185, 129), Color.White, 240, 36);
                btnAddNow.Location = new Point(280, 105);
                btnAddNow.Click += (s, e) => OpenManualAddDialog();
                emptyCard.Controls.Add(btnAddNow);

                flowPrinters.Controls.Add(emptyCard);
                flowPrinters.ResumeLayout();
                return;
            }

            foreach (var p in printers)
            {
                Panel card = new Panel();
                card.Size = new Size(cardWidth, 105);
                card.BackColor = Color.White;
                card.Margin = new Padding(0, 0, 0, 14);
                card.Paint += (s, e) =>
                {
                    ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                        Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
                };

                // Left color status strip (Green if installed, Red if Ricoh default)
                Panel leftBar = new Panel();
                leftBar.Dock = DockStyle.Left;
                leftBar.Width = 6;
                leftBar.BackColor = p.IsInstalled ? Color.FromArgb(16, 185, 129) : Color.FromArgb(207, 16, 45);
                card.Controls.Add(leftBar);

                // Right action buttons container (Docked Right: never collides with text!)
                FlowLayoutPanel actionPanel = new FlowLayoutPanel();
                actionPanel.Dock = DockStyle.Right;
                actionPanel.FlowDirection = FlowDirection.RightToLeft;
                actionPanel.WrapContents = false;
                actionPanel.Width = 460;
                actionPanel.Padding = new Padding(0, 28, 16, 0);
                actionPanel.BackColor = Color.Transparent;

                PrinterConfig currentPrinter = p;

                Button btnDelete = CreateFlatButton("🗑️ Delete", Color.FromArgb(254, 226, 226), Color.FromArgb(220, 38, 38), 85, 40);
                btnDelete.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                btnDelete.Margin = new Padding(6, 0, 0, 0);
                btnDelete.Click += (s, e) => DeletePrinter(currentPrinter);

                if (!p.IsInstalled)
                {
                    Button btnInstall = CreateFlatButton("📥 1-Click Install", Color.FromArgb(207, 16, 45), Color.White, 150, 40);
                    btnInstall.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    btnInstall.Click += (s, e) => ExecuteInstallPrinter(currentPrinter);
                    actionPanel.Controls.Add(btnInstall);
                    actionPanel.Controls.Add(btnDelete);
                }
                else
                {
                    btnDelete.Height = 40;
                    btnDelete.Width = 85;

                    Button btnTest = CreateFlatButton("🖨️ Test Page", Color.FromArgb(2, 132, 199), Color.White, 105, 40);
                    btnTest.Click += (s, e) => PrintTestPage(currentPrinter);
                    actionPanel.Controls.Add(btnTest);

                    Button btnDefault = CreateFlatButton("⭐ Set Default", Color.FromArgb(71, 85, 105), Color.White, 105, 40);
                    btnDefault.Click += (s, e) => SetAsDefault(currentPrinter);
                    actionPanel.Controls.Add(btnDefault);

                    Button btnReinstall = CreateFlatButton("🔄 Reinstall", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 90, 40);
                    btnReinstall.Click += (s, e) => ExecuteInstallPrinter(currentPrinter);
                    actionPanel.Controls.Add(btnReinstall);

                    actionPanel.Controls.Add(btnDelete);
                }

                // Middle Info Panel (Docked Fill: uses all remaining space between leftBar and actionPanel)
                Panel infoPanel = new Panel();
                infoPanel.Dock = DockStyle.Fill;
                infoPanel.BackColor = Color.Transparent;

                Label lblName = new Label();
                lblName.Text = p.displayName;
                lblName.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                lblName.ForeColor = Color.FromArgb(15, 23, 42);
                lblName.Location = new Point(16, 12);
                lblName.AutoSize = true;
                infoPanel.Controls.Add(lblName);

                Label lblDetails = new Label();
                lblDetails.Text = string.Format("Model: {0}  |  IP: {1}  |  Location: {2}", p.modelName, p.ip, p.location);
                lblDetails.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
                lblDetails.ForeColor = Color.FromArgb(100, 116, 139);
                lblDetails.Location = new Point(17, 40);
                lblDetails.AutoSize = true;
                infoPanel.Controls.Add(lblDetails);

                Label lblNetwork = new Label();
                if (p.IsOnline)
                {
                    lblNetwork.Text = "● Online (" + p.PingMs + "ms)";
                    lblNetwork.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    lblNetwork.Text = "○ Offline / Asleep";
                    lblNetwork.ForeColor = Color.FromArgb(148, 163, 184);
                }
                lblNetwork.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                lblNetwork.Location = new Point(17, 68);
                lblNetwork.AutoSize = true;
                infoPanel.Controls.Add(lblNetwork);

                Label lblInstallState = new Label();
                if (p.IsInstalled)
                {
                    lblInstallState.Text = "✔ Installed on this PC";
                    lblInstallState.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    lblInstallState.Text = "✖ Not Installed";
                    lblInstallState.ForeColor = Color.FromArgb(225, 29, 72);
                }
                lblInstallState.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                lblInstallState.Location = new Point(165, 68);
                lblInstallState.AutoSize = true;
                infoPanel.Controls.Add(lblInstallState);

                card.Controls.Add(infoPanel);
                card.Controls.Add(actionPanel);

                flowPrinters.Controls.Add(card);
            }

            flowPrinters.ResumeLayout();
        }

        private void DeletePrinter(PrinterConfig printer)
        {
            if (!printer.IsInstalled)
            {
                DialogResult dr = MessageBox.Show(
                    "Are you sure you want to remove '" + printer.displayName + "' (" + printer.ip + ") from this list?",
                    "Remove Printer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                {
                    printers.Remove(printer);
                    SaveConfiguration();
                    Log("Removed '" + printer.displayName + "' from printer list.");
                    RenderPrinterCards();
                }
            }
            else
            {
                DialogResult dr = MessageBox.Show(
                    "'" + printer.displayName + "' is currently installed on this PC.\n\n" +
                    "• Click [Yes] to remove from list AND uninstall from Windows.\n" +
                    "• Click [No] to remove from list only (keep Windows driver).\n" +
                    "• Click [Cancel] to abort.",
                    "Remove Printer",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Cancel) return;

                if (dr == DialogResult.Yes)
                {
                    Log("Uninstalling printer from Windows: " + printer.displayName);
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -ExecutionPolicy Bypass -Command \"Remove-Printer -Name '" + printer.displayName.Replace("'", "''") + "' -ErrorAction SilentlyContinue\"");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        Process.Start(psi).WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        Log("Error removing Windows printer: " + ex.Message);
                    }
                }

                printers.Remove(printer);
                SaveConfiguration();
                Log("Removed '" + printer.displayName + "' from printer list.");
                RefreshAllStatusAsync();
            }
        }

        private void ExecuteInstallPrinter(PrinterConfig printer)
        {
            string infFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, printer.infRelativePath);
            if (!File.Exists(infFullPath))
            {
                MessageBox.Show(
                    "Driver INF file not found at:\n" + infFullPath +
                    "\n\nPlease ensure the 'drivers' folder is beside this program.",
                    "Driver Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bottomContainer.Height = 180;
            btnToggleLog.Text = "✖ Hide Activity Log";

            lblStatus.Text = "Installing " + printer.displayName + "... Please wait...";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            btnRefresh.Enabled = false;
            btnScan.Enabled = false;
            btnManualAdd.Enabled = false;

            Log("==================================================");
            Log("START INSTALLATION: " + printer.displayName);
            Log("Target IP: " + printer.ip);
            Log("Driver Name: " + printer.modelName);
            Log("Driver INF: " + infFullPath);

            Task.Factory.StartNew(() =>
            {
                bool success = false;
                string errorMsg = "";

                try
                {
                    Log("[1/3] Staging driver with pnputil into Driver Store...");
                    ProcessStartInfo psiPnp = new ProcessStartInfo("pnputil.exe", "/add-driver \"" + infFullPath + "\" /install");
                    psiPnp.CreateNoWindow = true;
                    psiPnp.UseShellExecute = false;
                    psiPnp.RedirectStandardOutput = true;
                    psiPnp.RedirectStandardError = true;

                    using (Process pPnp = Process.Start(psiPnp))
                    {
                        string pnpOut = pPnp.StandardOutput.ReadToEnd();
                        pPnp.WaitForExit();
                        Log("pnputil: " + pnpOut.Trim().Replace("\r", " ").Replace("\n", " "));
                    }

                    Log("[2/3] Configuring TCP/IP port and Windows print queue...");
                    string psScript = string.Format(
                        "$ErrorActionPreference = 'Stop'; " +
                        "$portName = 'IP_{0}'; " +
                        "if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {{ Add-PrinterPort -Name $portName -PrinterHostAddress '{0}' }}; " +
                        "if (-not (Get-PrinterDriver -Name '{1}' -ErrorAction SilentlyContinue)) {{ Add-PrinterDriver -Name '{1}' }}; " +
                        "if (-not (Get-Printer -Name '{2}' -ErrorAction SilentlyContinue)) {{ Add-Printer -Name '{2}' -DriverName '{1}' -PortName $portName }} else {{ Set-Printer -Name '{2}' -DriverName '{1}' -PortName $portName }};",
                        printer.ip,
                        printer.modelName.Replace("'", "''"),
                        printer.displayName.Replace("'", "''")
                    );

                    ProcessStartInfo psiPs = new ProcessStartInfo(
                        "powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" + psScript + "\""
                    );
                    psiPs.CreateNoWindow = true;
                    psiPs.UseShellExecute = false;
                    psiPs.RedirectStandardOutput = true;
                    psiPs.RedirectStandardError = true;

                    using (Process pPs = Process.Start(psiPs))
                    {
                        string stdOut = pPs.StandardOutput.ReadToEnd();
                        string stdErr = pPs.StandardError.ReadToEnd();
                        pPs.WaitForExit();

                        if (!string.IsNullOrEmpty(stdOut)) Log("PowerShell: " + stdOut.Trim());
                        if (!string.IsNullOrEmpty(stdErr)) Log("PowerShell Warning/Error: " + stdErr.Trim());

                        if (pPs.ExitCode == 0)
                        {
                            success = true;
                        }
                        else
                        {
                            errorMsg = stdErr;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                }

                this.Invoke((Action)(() =>
                {
                    progressBar.Visible = false;
                    btnRefresh.Enabled = true;
                    btnScan.Enabled = true;
                    btnManualAdd.Enabled = true;

                    if (success)
                    {
                        Log("[3/3] SUCCESS: " + printer.displayName + " is installed and ready!");
                        lblStatus.Text = printer.displayName + " installed successfully!";
                        RefreshAllStatusAsync();

                        DialogResult dr = MessageBox.Show(
                            printer.displayName + " has been successfully configured on this PC!\n(Queue pointing to IP " + printer.ip + ")\n\nWould you like to print a test page now?",
                            "Configuration Successful",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (dr == DialogResult.Yes)
                        {
                            PrintTestPage(printer);
                        }
                    }
                    else
                    {
                        Log("ERROR during installation: " + errorMsg);
                        lblStatus.Text = "Installation failed. Check log.";
                        MessageBox.Show(
                            "An error occurred while installing the printer:\n\n" + errorMsg +
                            "\n\nPlease ensure this application was run as Administrator.",
                            "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            });
        }

        private void PrintTestPage(PrinterConfig printer)
        {
            try
            {
                Log("Sending Windows test page to: " + printer.displayName);
                ProcessStartInfo psi = new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /k /n \"" + printer.displayName + "\"");
                Process.Start(psi);
                lblStatus.Text = "Test page sent to " + printer.displayName;
            }
            catch (Exception ex)
            {
                Log("Error printing test page: " + ex.Message);
            }
        }

        private void SetAsDefault(PrinterConfig printer)
        {
            try
            {
                string cmd = string.Format("(New-Object -ComObject WScript.Network).SetDefaultPrinter('{0}')", printer.displayName.Replace("'", "''"));
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + cmd + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi).WaitForExit();

                Log("Set default printer to: " + printer.displayName);
                lblStatus.Text = "Default printer set to " + printer.displayName;
                MessageBox.Show(printer.displayName + " is now your default printer.", "Default Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Error setting default printer: " + ex.Message);
            }
        }

        private void OpenManualAddDialog()
        {
            using (ManualAddForm mf = new ManualAddForm())
            {
                if (mf.ShowDialog(this) == DialogResult.OK && mf.CreatedPrinter != null)
                {
                    printers.Add(mf.CreatedPrinter);
                    SaveConfiguration();
                    Log("Added manual printer: " + mf.CreatedPrinter.displayName + " (" + mf.CreatedPrinter.ip + ")");
                    RenderPrinterCards();

                    if (mf.InstallImmediately)
                    {
                        ExecuteInstallPrinter(mf.CreatedPrinter);
                    }
                }
            }
        }

        private void OpenScannerDialog()
        {
            using (ScannerForm sf = new ScannerForm(printers, configPath))
            {
                if (sf.ShowDialog(this) == DialogResult.OK)
                {
                    LoadConfiguration();
                    RefreshAllStatusAsync();
                }
            }
        }

        public void Log(string message)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, message);
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => txtLog.AppendText(line)));
            }
            else
            {
                txtLog.AppendText(line);
            }
        }
    }

    public class ManualAddForm : Form
    {
        private ComboBox cmbModel;
        private TextBox txtIp;
        private TextBox txtDisplayName;
        private TextBox txtLocation;
        private Button btnInstallNow;
        private Button btnAddToList;
        private List<ModelProfile> profiles;

        public PrinterConfig CreatedPrinter { get; private set; }
        public bool InstallImmediately { get; private set; }

        public ManualAddForm()
        {
            profiles = ModelProfile.LoadFromDriversFolder(AppDomain.CurrentDomain.BaseDirectory);
            InitializeManualAddUI();
        }

        private void InitializeManualAddUI()
        {
            this.Text = "Add Printer Manually (Offline Pre-Configuration)";
            this.Size = new Size(640, 430);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblHeading = new Label();
            lblHeading.Text = "Pre-Configure Printer & Driver (No Network Required)";
            lblHeading.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblHeading.ForeColor = Color.FromArgb(15, 23, 42);
            lblHeading.Location = new Point(24, 18);
            lblHeading.AutoSize = true;
            this.Controls.Add(lblHeading);

            Label lblSub = new Label();
            lblSub.Text = "Discovered " + profiles.Count + " Ricoh models from your 'drivers' folder.\nSet the planned IP and install the driver now, so when the network is connected\nlater, the machine prints immediately without reinstalling.";
            lblSub.ForeColor = Color.FromArgb(100, 116, 139);
            lblSub.Location = new Point(25, 46);
            lblSub.AutoSize = true;
            this.Controls.Add(lblSub);

            // Model Selection
            Label lblModel = new Label();
            lblModel.Text = "Ricoh Model:";
            lblModel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblModel.Location = new Point(25, 115);
            lblModel.AutoSize = true;
            this.Controls.Add(lblModel);

            cmbModel = new ComboBox();
            cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var p in profiles) cmbModel.Items.Add(p);
            cmbModel.SelectedIndex = 0;
            cmbModel.Location = new Point(160, 112);
            cmbModel.Size = new Size(430, 28);
            cmbModel.SelectedIndexChanged += (s, e) => AutoUpdateDisplayName();
            this.Controls.Add(cmbModel);

            // IP Address
            Label lblIp = new Label();
            lblIp.Text = "Target IP Address:";
            lblIp.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblIp.Location = new Point(25, 160);
            lblIp.AutoSize = true;
            this.Controls.Add(lblIp);

            txtIp = new TextBox();
            txtIp.Text = "192.168.1.200";
            txtIp.Location = new Point(160, 157);
            txtIp.Size = new Size(200, 26);
            txtIp.TextChanged += (s, e) => AutoUpdateDisplayName();
            this.Controls.Add(txtIp);

            // Display Name
            Label lblName = new Label();
            lblName.Text = "Printer Name:";
            lblName.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblName.Location = new Point(25, 205);
            lblName.AutoSize = true;
            this.Controls.Add(lblName);

            txtDisplayName = new TextBox();
            txtDisplayName.Text = "Ricoh MFP (200)";
            txtDisplayName.Location = new Point(160, 202);
            txtDisplayName.Size = new Size(430, 26);
            this.Controls.Add(txtDisplayName);

            // Location
            Label lblLoc = new Label();
            lblLoc.Text = "Location / Client:";
            lblLoc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblLoc.Location = new Point(25, 250);
            lblLoc.AutoSize = true;
            this.Controls.Add(lblLoc);

            txtLocation = new TextBox();
            txtLocation.Text = "Main Office";
            txtLocation.Location = new Point(160, 247);
            txtLocation.Size = new Size(430, 26);
            this.Controls.Add(txtLocation);

            // Buttons
            btnInstallNow = new Button();
            btnInstallNow.Text = "📥 Install Driver & Queue Now";
            btnInstallNow.Location = new Point(160, 315);
            btnInstallNow.Size = new Size(230, 40);
            btnInstallNow.BackColor = Color.FromArgb(207, 16, 45);
            btnInstallNow.ForeColor = Color.White;
            btnInstallNow.FlatStyle = FlatStyle.Flat;
            btnInstallNow.FlatAppearance.BorderSize = 0;
            btnInstallNow.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnInstallNow.Cursor = Cursors.Hand;
            btnInstallNow.Click += (s, e) => SaveAndClose(true);
            this.Controls.Add(btnInstallNow);

            btnAddToList = new Button();
            btnAddToList.Text = "+ Add to List Only";
            btnAddToList.Location = new Point(400, 315);
            btnAddToList.Size = new Size(190, 40);
            btnAddToList.BackColor = Color.FromArgb(51, 65, 85);
            btnAddToList.ForeColor = Color.White;
            btnAddToList.FlatStyle = FlatStyle.Flat;
            btnAddToList.FlatAppearance.BorderSize = 0;
            btnAddToList.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnAddToList.Cursor = Cursors.Hand;
            btnAddToList.Click += (s, e) => SaveAndClose(false);
            this.Controls.Add(btnAddToList);

            AutoUpdateDisplayName();
        }

        private void AutoUpdateDisplayName()
        {
            if (cmbModel.SelectedItem == null) return;
            ModelProfile p = (ModelProfile)cmbModel.SelectedItem;
            string ip = txtIp.Text.Trim();
            string last = ip.Contains(".") ? ip.Substring(ip.LastIndexOf('.') + 1) : ip;
            txtDisplayName.Text = p.Prefix + " (" + last + ")";
        }

        private void SaveAndClose(bool installNow)
        {
            string ip = txtIp.Text.Trim();
            if (string.IsNullOrEmpty(ip) || !ip.Contains("."))
            {
                MessageBox.Show("Please enter a valid IP address.", "Invalid IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ModelProfile prof = (ModelProfile)cmbModel.SelectedItem;
            string dName = txtDisplayName.Text.Trim();
            if (string.IsNullOrEmpty(dName)) dName = prof.Prefix + " (" + ip + ")";

            string loc = txtLocation.Text.Trim();
            if (string.IsNullOrEmpty(loc)) loc = "Client Office (" + ip + ")";

            CreatedPrinter = new PrinterConfig
            {
                id = "manual_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                displayName = dName,
                modelName = prof.DriverName,
                ip = ip,
                infRelativePath = prof.InfPath,
                location = loc
            };

            InstallImmediately = installNow;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    public class ScannerForm : Form
    {
        private List<PrinterConfig> printers;
        private string configPath;
        private ListView lvResults;
        private ProgressBar pbScan;
        private Label lblScanStatus;
        private Button btnStartScan;
        private Button btnAddSelected;
        private ComboBox cmbModel;
        private TextBox txtSubnet;
        private List<ModelProfile> profiles;

        public ScannerForm(List<PrinterConfig> existingPrinters, string cfgPath)
        {
            this.printers = existingPrinters;
            this.configPath = cfgPath;
            this.profiles = ModelProfile.LoadFromDriversFolder(AppDomain.CurrentDomain.BaseDirectory);
            InitializeScannerUI();
        }

        private void InitializeScannerUI()
        {
            this.Text = "Network Printer Auto-Discovery";
            this.Size = new Size(860, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblInfo = new Label();
            lblInfo.Text = "Scans your office network to automatically detect Ricoh printer models (via Port 9100 / Web Image Monitor).\nLoaded " + profiles.Count + " drivers dynamically from your 'drivers' folder.";
            lblInfo.Location = new Point(20, 12);
            lblInfo.AutoSize = true;
            this.Controls.Add(lblInfo);

            Label lblSub = new Label();
            lblSub.Text = "Subnet:";
            lblSub.Location = new Point(20, 52);
            lblSub.AutoSize = true;
            this.Controls.Add(lblSub);

            txtSubnet = new TextBox();
            txtSubnet.Text = "192.168.23";
            txtSubnet.Location = new Point(80, 49);
            txtSubnet.Size = new Size(110, 25);
            this.Controls.Add(txtSubnet);

            btnStartScan = new Button();
            btnStartScan.Text = "🔍 Start Fast Scan (1 - 254)";
            btnStartScan.Location = new Point(205, 47);
            btnStartScan.Size = new Size(190, 29);
            btnStartScan.BackColor = Color.FromArgb(207, 16, 45);
            btnStartScan.ForeColor = Color.White;
            btnStartScan.FlatStyle = FlatStyle.Flat;
            btnStartScan.FlatAppearance.BorderSize = 0;
            btnStartScan.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnStartScan.Click += (s, e) => StartSubnetScan();
            this.Controls.Add(btnStartScan);

            pbScan = new ProgressBar();
            pbScan.Location = new Point(410, 49);
            pbScan.Size = new Size(410, 25);
            pbScan.Maximum = 254;
            this.Controls.Add(pbScan);

            lblScanStatus = new Label();
            lblScanStatus.Text = "Ready to scan.";
            lblScanStatus.Location = new Point(20, 84);
            lblScanStatus.AutoSize = true;
            lblScanStatus.ForeColor = Color.FromArgb(100, 116, 139);
            this.Controls.Add(lblScanStatus);

            lvResults = new ListView();
            lvResults.View = View.Details;
            lvResults.FullRowSelect = true;
            lvResults.GridLines = true;
            lvResults.Location = new Point(20, 112);
            lvResults.Size = new Size(800, 290);
            lvResults.Columns.Add("IP Address", 130);
            lvResults.Columns.Add("Detected Ricoh Model", 210);
            lvResults.Columns.Add("Port 9100", 85);
            lvResults.Columns.Add("Current Office Status", 340);
            lvResults.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            this.Controls.Add(lvResults);

            Label lblAssign = new Label();
            lblAssign.Text = "Assign Model:";
            lblAssign.Location = new Point(20, 432);
            lblAssign.AutoSize = true;
            this.Controls.Add(lblAssign);

            cmbModel = new ComboBox();
            cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var prof in profiles)
            {
                cmbModel.Items.Add(prof);
            }
            if (cmbModel.Items.Count > 0) cmbModel.SelectedIndex = 0;
            cmbModel.Location = new Point(125, 428);
            cmbModel.Size = new Size(440, 28);
            this.Controls.Add(cmbModel);

            btnAddSelected = new Button();
            btnAddSelected.Text = "+ Add to Main List";
            btnAddSelected.Location = new Point(585, 427);
            btnAddSelected.Size = new Size(235, 32);
            btnAddSelected.BackColor = Color.FromArgb(16, 185, 129);
            btnAddSelected.ForeColor = Color.White;
            btnAddSelected.FlatStyle = FlatStyle.Flat;
            btnAddSelected.FlatAppearance.BorderSize = 0;
            btnAddSelected.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnAddSelected.Click += (s, e) => AddSelectedPrinter();
            this.Controls.Add(btnAddSelected);
        }

        private void OnSelectionChanged()
        {
            if (lvResults.SelectedItems.Count == 0) return;
            string detectedModel = lvResults.SelectedItems[0].SubItems[1].Text;

            // Normalize detected model
            string cleanDetected = detectedModel.Replace("RICOH", "").Trim();

            for (int i = 0; i < profiles.Count; i++)
            {
                if (!string.IsNullOrEmpty(cleanDetected) &&
                    (profiles[i].DriverName.IndexOf(cleanDetected, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cleanDetected.IndexOf(profiles[i].Prefix, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    cmbModel.SelectedIndex = i;
                    return;
                }
            }
        }

        private void StartSubnetScan()
        {
            string subnet = txtSubnet.Text.Trim().TrimEnd('.');
            btnStartScan.Enabled = false;
            btnAddSelected.Enabled = false;
            lvResults.Items.Clear();
            pbScan.Value = 0;
            lblScanStatus.Text = "Scanning " + subnet + ".1 to " + subnet + ".254 on Port 9100...";

            Task.Factory.StartNew(() =>
            {
                int completed = 0;
                List<string> foundIps = new List<string>();

                Parallel.For(1, 255, new ParallelOptions { MaxDegreeOfParallelism = 40 }, i =>
                {
                    string targetIp = subnet + "." + i;
                    bool isOpen = false;

                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(targetIp, 9100, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(350, false))
                            {
                                if (client.Connected)
                                {
                                    client.EndConnect(ar);
                                    isOpen = true;
                                }
                            }
                        }
                    }
                    catch {}

                    System.Threading.Interlocked.Increment(ref completed);

                    if (isOpen)
                    {
                        lock (foundIps) { foundIps.Add(targetIp); }

                        string detectedModel = DetectRicohModel(targetIp);

                        this.Invoke((Action)(() =>
                        {
                            string match = "Unlisted / Ready to Add";
                            foreach (var p in printers)
                            {
                                if (p.ip == targetIp)
                                {
                                    match = "In Main List (" + p.displayName + ")";
                                    break;
                                }
                            }

                            ListViewItem lvi = new ListViewItem(targetIp);
                            lvi.SubItems.Add(detectedModel);
                            lvi.SubItems.Add("OPEN");
                            lvi.SubItems.Add(match);
                            lvi.ForeColor = Color.FromArgb(16, 185, 129);
                            lvi.Tag = detectedModel;
                            lvResults.Items.Add(lvi);
                        }));
                    }

                    if (completed % 10 == 0 || completed == 254)
                    {
                        this.Invoke((Action)(() =>
                        {
                            pbScan.Value = Math.Min(completed, 254);
                        }));
                    }
                });

                this.Invoke((Action)(() =>
                {
                    pbScan.Value = 254;
                    btnStartScan.Enabled = true;
                    btnAddSelected.Enabled = true;
                    lblScanStatus.Text = "Scan Complete! Found " + foundIps.Count + " active printer device(s).";
                }));
            });
        }

        private string DetectRicohModel(string ip)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(ip, 9100, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(800, false) && client.Connected)
                    {
                        client.EndConnect(ar);
                        using (NetworkStream ns = client.GetStream())
                        {
                            ns.ReadTimeout = 1200;
                            ns.WriteTimeout = 800;
                            byte[] cmd = Encoding.ASCII.GetBytes("@PJL INFO ID\r\n");
                            ns.Write(cmd, 0, cmd.Length);

                            byte[] buf = new byte[1024];
                            int read = ns.Read(buf, 0, buf.Length);
                            string resp = Encoding.ASCII.GetString(buf, 0, read);
                            Match m = Regex.Match(resp, "\"([^\"]+)\"");
                            if (m.Success)
                            {
                                return m.Groups[1].Value.Trim();
                            }
                        }
                    }
                }
            }
            catch {}

            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://" + ip + "/web/guest/en/websys/webArch/header.cgi");
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string html = sr.ReadToEnd();
                    Match m = Regex.Match(html, "<h2\\s+id=[\"']modelName[\"'][^>]*>([^<]+)</h2>", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        string name = m.Groups[1].Value.Trim();
                        return name.StartsWith("RICOH", StringComparison.OrdinalIgnoreCase) ? name : "RICOH " + name;
                    }

                    Match mGen = Regex.Match(html, "(MP\\s*C\\d+|IM\\s*C\\d+|Aficio\\s*MP\\s*C\\d+)", RegexOptions.IgnoreCase);
                    if (mGen.Success)
                    {
                        return "RICOH " + mGen.Groups[1].Value.Trim();
                    }
                }
            }
            catch {}

            return "Ricoh MFP (Port 9100)";
        }

        private void AddSelectedPrinter()
        {
            if (lvResults.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select an IP address from the list above.", "Select Device", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string ip = lvResults.SelectedItems[0].Text;
            ModelProfile prof = (ModelProfile)cmbModel.SelectedItem;

            foreach (var p in printers)
            {
                if (p.ip == ip)
                {
                    MessageBox.Show("Printer with IP " + ip + " is already in the main list (" + p.displayName + ").", "Already Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            string lastOctet = ip.Substring(ip.LastIndexOf('.') + 1);
            PrinterConfig newPrinter = new PrinterConfig
            {
                id = "custom_" + ip.Replace(".", "_"),
                displayName = prof.Prefix + " (" + lastOctet + ")",
                modelName = prof.DriverName,
                ip = ip,
                infRelativePath = prof.InfPath,
                location = "Office Network (" + ip + ")"
            };

            printers.Add(newPrinter);
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(configPath, js.Serialize(printers));
            }
            catch {}

            MessageBox.Show("Added " + newPrinter.displayName + " (" + ip + ") to the main printer list!", "Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
