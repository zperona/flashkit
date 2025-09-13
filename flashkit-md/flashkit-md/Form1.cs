using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace flashkit_md
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.Text = this.Text + " " + this.ProductVersion;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
             
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void btn_check_Click(object sender, EventArgs e)
        {
            using (DeviceSession ds = new DeviceSession())
            {
                consWriteLine("-----------------------------------------------------");
                int ram_size;
                if (!ds.IsConnected)
                {
                    consWriteLine("Device is not connected");
                    return;
                }
                consWriteLine("Connected to: " + Device.getPortName());
                consWriteLine("ROM name : " + Cart.getRomName());
                consWriteLine("ROM size : " + Cart.GetRomSize() / 1024 + "K");
                ram_size = Cart.getRamSize();
                if (ram_size < 1024)
                {
                    consWriteLine("RAM size : " + ram_size + "B");
                }
                else
                {
                    consWriteLine("RAM size : " + ram_size / 1024 + "K");
                }
                ds.Dispose();
            }
        }
        void consWrite(string str)
        {
            consoleBox.AppendText(str);
        }

        void consWriteLine(string str)
        {
            consoleBox.AppendText(str + "\r\n");
        }

        private void btn_rd_ram_Click(object sender, EventArgs e)
        {

            try
            {

                int ram_size;
                byte[] ram;
                Device.connect();
                Device.setDelay(1);
                string rom_name = Cart.getRomName();
                rom_name += ".srm";
                saveFileDialog1.FileName = rom_name;
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    consWriteLine("-----------------------------------------------------");
                    ram_size = Cart.getRamSize();
                    if (ram_size == 0) throw new Exception("RAM is not detected");
                    consWriteLine("Read RAM to " + saveFileDialog1.FileName);
                    if (ram_size < 1024)
                    {
                        consWriteLine("RAM size : " + ram_size + "B");
                    }
                    else
                    {
                        consWriteLine("RAM size : " + ram_size / 1024 + "K");
                    }
                    Device.writeWord(0xA13000, 0xffff);
                    Device.setAddr(0x200000);
                    ram = new byte[ram_size * 2];
                    Device.read(ram, 0, ram.Length);

                    FileStream f = File.OpenWrite(saveFileDialog1.FileName);
                    f.Write(ram, 0, ram.Length);
                    f.Close();
                    printMD5(ram);
                    consWriteLine("OK");
                }

            }
            catch (Exception x)
            {
                consWriteLine(x.Message);
            }
            Device.disconnect();
        }

        private void btn_rd_rom_Click(object sender, EventArgs e)
        {
            try
            {
                using (var session = new DeviceSession())
                {
                    if (!session.IsConnected)
                    {
                        consWriteLine("Device not detected.");
                        return;
                    }
                    session.SetDelay(1);

                    string romName = Cart.getRomName() + ".bin";
                    saveFileDialog1.FileName = romName;
                    if (saveFileDialog1.ShowDialog() != DialogResult.OK) return;

                    int romSize = Cart.GetRomSize();
                    consWriteLine($"ROM size: {romSize / 1024} KB");

                    var flashHelpers = new MX29GL128Helpers(session, consWriteLine);

                    progressBar1.Value = 0;
                    progressBar1.Maximum = romSize;

                    flashHelpers.ReadRom(saveFileDialog1.FileName, romSize, consWriteLine, addr =>
                    {
                        progressBar1.Value = Math.Min(progressBar1.Maximum, addr);
                        this.Update();
                    });

                    consWriteLine("ROM read successfully.");
                }
            }
            catch (Exception ex)
            {
                consWriteLine("Error: " + ex.Message);
            }
            finally
            {
                Device.disconnect();
                progressBar1.Value = 0;
            }
        }

        private void printMD5(byte []buff)
        {
            MD5 hash = MD5.Create();
            byte[] hash_data = hash.ComputeHash(buff);
            consWriteLine("MD5: " + BitConverter.ToString(hash_data));
        }
        
        private void btn_wr_ram_Click(object sender, EventArgs e)
        {
            try
            {

                int ram_size;
                int copy_len;
                byte[] ram;
                Device.connect();
                Device.setDelay(1);
                
                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    consWriteLine("-----------------------------------------------------");
                    consWriteLine("Write RAM...");
                    this.Update();
                    FileStream f = File.OpenRead(openFileDialog1.FileName);
                    ram = new byte[f.Length];
                    f.Read(ram, 0, ram.Length);
                    f.Close();

                    ram_size = Cart.getRamSize();
                    if (ram_size == 0) throw new Exception("RAM is not detected");
                    this.Update();

                    ram_size *= 2;
                    copy_len = ram.Length;
                    if (ram_size < copy_len) copy_len = ram_size;
                    if (copy_len % 2 != 0) copy_len--;
                    Device.writeWord(0xA13000, 0xffff);
                    Device.setAddr(0x200000);
                    Device.write(ram, 0, copy_len);
                    consWriteLine("Verify...");
                    this.Update();
                    byte[] ram2 = new byte[copy_len];
                    Device.setAddr(0x200000);
                    Device.read(ram2, 0, copy_len);
                    for (int i = 0; i < copy_len; i++)
                    {
                        if (i % 2 == 0) continue;
                        if (ram[i] != ram2[i]) throw new Exception("Verify error at " + i);
                    }

                    copy_len /= 2;
                    consWriteLine("" + copy_len+ " words sent");

                    printMD5(ram);
                    consWriteLine("OK");
                }

            }
            catch (Exception x)
            {
                consWriteLine(x.Message);
            }
            Device.disconnect();
        }

        private void btn_wr_rom_Click(object sender, EventArgs e)
        {
            try
            {
                openFileDialog1.Filter = "MD or BIN Files (*.md;*.bin)|*.md;*.bin|All Files (*.*)|*.*";

                if (openFileDialog1.ShowDialog() != DialogResult.OK)
                    return;

                byte[] rom = File.ReadAllBytes(openFileDialog1.FileName);

                int romSize = rom.Length;
                if (romSize % 0x20000 != 0)
                    romSize = (romSize / 0x20000 + 1) * 0x20000;
               
                Array.Resize(ref rom, romSize);

                consWriteLine($"ROM size: {romSize / 1024} KB");

                using (var session = new DeviceSession())
                {
                    if (!session.IsConnected)
                    {
                        consWriteLine("Device not detected.");
                        return;
                    }

                    // Create helpers and pass logging callback
                    var flashHelpers = new MX29GL128Helpers(session, consWriteLine);

                    progressBar1.Value = 0;
                    progressBar1.Maximum = romSize;

                    // --- ERASE ---
                    consWriteLine("Flash reset + unlock...");
                    session.FlashResetByPass();
                    session.FlashUnlockBypass();

                    consWriteLine("Erasing sectors...");
                    flashHelpers.EraseAllSectors(consWriteLine, session);

                    // --- WRITE ---
                    consWriteLine("Writing ROM...");
                    DateTime t0 = DateTime.Now;

                    flashHelpers.WriteROM(rom, consWriteLine, session, addr =>
                    {
                        progressBar1.Value = Math.Min(progressBar1.Maximum, addr);
                        this.Update();
                    });

                    session.FlashResetByPass();
                    double sec = (DateTime.Now - t0).TotalSeconds;
                    consWriteLine($"Write completed in {sec:F1} sec");

                    // --- VERIFY ---
                    consWriteLine("Verifying ROM...");
                    flashHelpers.VerifyROM(rom, consWriteLine, session, addr =>
                    {
                        progressBar1.Value = Math.Min(progressBar1.Maximum, addr);
                        this.Update();
                    });

                    progressBar1.Value = romSize;
                    consWriteLine("ROM write complete.");
                }
            }
            catch (Exception ex)
            {
                consWriteLine("Error: " + ex.Message);
            }
            finally
            {
                progressBar1.Value = 0;
            }
        }

    }
}
