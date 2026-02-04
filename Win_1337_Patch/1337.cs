using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Win_1337_Patch
{
    public partial class Form1 : Form
    {
        private string exe = String.Empty;
        private string f1337 = String.Empty;

        public Form1()
        {
            InitializeComponent();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string ver = "v" + version.Major + "." + version.Minor;
            this.Text = "Win 1337 Apply Patch File " + ver;
            linkdfox.Text = ver + " By DeltaFoX";
        }

        private void set()
        {
            try
            {
                t1337.Text = Ellipsis.Compact(f1337, t1337, EllipsisFormat.Path);
                toolTip1.SetToolTip(t1337, f1337);
                Properties.Settings.Default["url1337"] = f1337;
                Properties.Settings.Default.Save();

                string[] lines = File.ReadAllLines(f1337);
                if (!check_Symbol(lines[0]))
                    return;

                // Preserve original case for file dialog and filter (fixes case sensitivity issues on Windows 10)
                // Reference: https://github.com/Deltafox79/Win_1337_Apply_Patch/issues/4
                string unfOriginal = lines[0].Substring(1).Trim();
                string unfLower = unfOriginal.ToLower();
                string nf = Path.GetFileName(unfOriginal);
                string nfLower = Path.GetFileName(unfLower);
                string ext = Path.GetExtension(unfOriginal);
                OpenFileDialog apriDialogoFile1 = new OpenFileDialog
                {
                    FileName = nf,
                    Filter = "File " + ext + "|" + nf,
                    FilterIndex = 0,
                    Title = "Select the file \"" + nf + "\" File..."
                };

                if (apriDialogoFile1.ShowDialog() == DialogResult.OK)
                {
                    exe = apriDialogoFile1.FileName;
                    texe.Text = Ellipsis.Compact(Path.GetFileName(exe), texe, EllipsisFormat.Path);
                    toolTip1.SetToolTip(texe, exe);
                    Properties.Settings.Default["urlexe"] = exe;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    t1337.Text = "Select a .1337 File...";
                    texe.Text = "Select the Exe/Dll to Patch...";
                    f1337 = String.Empty;
                    exe = String.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while setting up the file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void t1337_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                f1337 = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
                set();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while processing drag and drop: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnSelect1337_Click(object sender, EventArgs e)
        {
            try
            {
                string url1337 = Properties.Settings.Default["url1337"].ToString();
                OpenFileDialog apriDialogoFile1 = new OpenFileDialog
                {
                    Filter = "File 1337|*.*",
                    FilterIndex = 0,
                    Title = "Select the .1337 File...",
                    InitialDirectory = url1337 != "" ? url1337 : Directory.GetCurrentDirectory() + "\\",
                    RestoreDirectory = true
                };
                if (apriDialogoFile1.ShowDialog() == DialogResult.OK)
                {
                    f1337 = apriDialogoFile1.FileName;
                    set();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while selecting the .1337 file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void t1337_DragEnter(object sender, DragEventArgs e)
        {
            try
            {
                e.Effect = DragDropEffects.All;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during DragEnter: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool check_Symbol(string s)
        {
            if (!s.StartsWith(">"))
            {
                MessageBox.Show("The .1337 File is not valid...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        private void Patch_Click(object sender, EventArgs e)
        {
            if (f1337 == String.Empty)
            {
                MessageBox.Show("Select a .1337 File...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                DFoX_Patch();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A problem occurred when patching: {ex.Message}", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DFoX_Patch()
        {
            if (string.IsNullOrWhiteSpace(f1337) || string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show("Select a .1337 File and the Exe/Dll to Patch...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!File.Exists(exe) || !File.Exists(f1337))
            {
                MessageBox.Show("Files are no Longer Present...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var request = new PatchRequest(f1337, exe, cfileoffsett.Checked, controlloBackup.Checked, cchangeOwnership.Checked);
            var outcome = PatchEngine.ApplyPatch(request);

            if (outcome.Success)
                MessageBox.Show(outcome.Message, "Info...", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(outcome.Message, "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void DFoX_Load(object sender, EventArgs e)
        {
            try
            {
                string urlexe = Properties.Settings.Default["urlexe"].ToString().Trim();
                string url1337 = Properties.Settings.Default["url1337"].ToString().Trim();
                cfileoffsett.Checked = (bool)Properties.Settings.Default["fixoffset"];
                controlloBackup.Checked = (bool)Properties.Settings.Default["backup"];
                cchangeOwnership.Checked = (bool)Properties.Settings.Default["changeOwnership"];

                if (urlexe != "")
                {
                    texe.Text = Ellipsis.Compact(Path.GetFileName(urlexe), texe, EllipsisFormat.Path);
                    toolTip1.SetToolTip(texe, urlexe);
                    exe = urlexe;
                }
                else
                    texe.Text = "Select the Exe/Dll to Patch...";

                if (url1337 != "" && urlexe != "")
                {
                    t1337.Text = Ellipsis.Compact(url1337, t1337, EllipsisFormat.Path);
                    toolTip1.SetToolTip(t1337, url1337);
                    f1337 = url1337;
                }
                else
                    t1337.Text = "Select a .1337 File...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during form load: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cfileoffsett_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["fixoffset"] = cfileoffsett.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving fix offset setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void controlloBackup_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["backup"] = controlloBackup.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving backup setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cchangeOwnership_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["changeOwnership"] = cchangeOwnership.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving change ownership setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void linkdfox_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                _apriUrl(@"https://github.com/Deltafox79/Win_1337_Apply_Patch");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while opening the URL: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void _apriUrl(string url)
        {
            try
            {
                string browserPath = ottieniLaPathBrowser();
                if (browserPath == string.Empty)
                    browserPath = "iexplore";
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo(browserPath)
                    {
                        Arguments = url
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while opening the browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ottieniLaPathBrowser()
        {
            string name = String.Empty;
            RegistryKey regKey = null;
            try
            {
                var regDefault = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.htm\\UserChoice", false);
                var stringDefault = regDefault.GetValue("ProgId");

                regKey = Registry.ClassesRoot.OpenSubKey(stringDefault + "\\shell\\open\\command", false);
                name = regKey.GetValue(null).ToString().ToLower().Replace("" + (char)34, "");

                if (!name.EndsWith("exe"))
                    name = name.Substring(0, name.LastIndexOf(".exe") + 4);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while retrieving browser path: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (regKey != null)
                    regKey.Close();
            }
            return name;
        }

        private void t1337_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                btnSelect1337.PerformClick();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during double click: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
