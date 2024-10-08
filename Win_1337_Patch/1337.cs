﻿using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Win_1337_Patch
{
    public partial class Form1 : Form
    {
        private string exe = String.Empty;
        private string f1337 = String.Empty;

        [System.Runtime.InteropServices.DllImport("Imagehlp.dll")]
        private static extern bool ImageRemoveCertificate(IntPtr handle, int index);

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

                string unf = lines[0].Substring(1).ToLower().Trim();
                string nf = Path.GetFileName(unf);
                string ext = Path.GetExtension(unf);
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
            if (!File.Exists(exe) || !File.Exists(f1337))
            {
                MessageBox.Show("Files are no Longer Present...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (cchangeOwnership.Checked)
            {
                try
                {
                    UnlockDLL(exe);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while changing ownership: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            string[] lines = File.ReadAllLines(f1337);
            if (!check_Symbol(lines[0]))
                return;
            if (lines[0].Substring(1).ToLower().Trim() != Path.GetFileName(exe).ToLower().Trim())
            {
                MessageBox.Show("The .1337 File is not valid for selected exe/dll...\n\n(\"" + lines[0].Substring(1).ToLower() + "\" but you have selected \"" + Path.GetFileName(exe).ToLower() + "\")", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            byte[] bexe = File.ReadAllBytes(exe);
            bool ok = true;
            for (var i = 1; i < lines.Length; i += 1)
            {
                if (lines[i].Trim() != "")
                {
                    string[] tmp = lines[i].Split(':');
                    int offsetHex = int.Parse(tmp[0], System.Globalization.NumberStyles.HexNumber) - (cfileoffsett.Checked ? 0xC00 : 0);
                    string[] tmp2 = tmp[1].Replace("->", ":").Split(':');
                    byte e = bexe[offsetHex];
                    byte f = byte.Parse(tmp2[0], System.Globalization.NumberStyles.HexNumber);
                    if (bexe[offsetHex] == byte.Parse(tmp2[0], System.Globalization.NumberStyles.HexNumber))
                        bexe[offsetHex] = byte.Parse(tmp2[1], System.Globalization.NumberStyles.HexNumber);
                    else
                    {
                        MessageBox.Show("Offset [" + offsetHex.ToString("X") + "] Wrong...\n\nSet 0x" + bexe[offsetHex].ToString("X") + " -> I expected 0x" + byte.Parse(tmp2[0], System.Globalization.NumberStyles.HexNumber).ToString("X"), "Error...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ok = false;
                        break;
                    }
                }
            }
            if (ok)
            {
                if (controlloBackup.Checked == true)
                {
                    string dateSuffix = DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss-tt");
                    string backupFileName = $"{exe}.{dateSuffix}.BAK";

                    if (File.Exists(backupFileName))
                        File.Delete(backupFileName);
                    File.Copy(exe, backupFileName);
                }
                if (File.Exists(exe))
                    File.Delete(exe);
                File.WriteAllBytes(exe, bexe);
                SistemaPeCks(exe);
                MessageBox.Show("File " + Path.GetFileName(exe) + " Patched...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SistemaPeCks(string file)
        {
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite))
                {
                    ImageRemoveCertificate(fs.SafeFileHandle.DangerousGetHandle(), 0);
                }

                checked
                {
                    mCheckSum PE = new mCheckSum();
                    PE.FixCheckSum(file);
                }
            }
            catch (OverflowException ex)
            {
                MessageBox.Show($"Overflow error occurred while processing PE checksum: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while processing PE checksum: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private void UnlockDLL(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("The specified file does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    using (StreamWriter sw = process.StandardInput)
                    {
                        if (sw.BaseStream.CanWrite)
                        {
                            sw.WriteLine($"takeown /F \"{filePath}\"");
                            sw.WriteLine($"icacls \"{filePath}\" /grant Administrators:F");
                        }
                    }

                    process.WaitForExit();
                }

                MessageBox.Show($"Ownership and permissions of {filePath} have been successfully changed.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while changing ownership: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
