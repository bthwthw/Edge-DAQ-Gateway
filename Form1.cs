using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows.Forms;
using HidLibrary;

namespace DLMT_BTL
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. KHAI BÁO BIẾN TOÀN CỤC
        // ==========================================
        private HidDevice stm32Device;

        private UdpClient udpClient;

        private bool isLogging = false; // Cờ kiểm soát việc ghi file CSV
        private StreamWriter logWriter;
        private object logLock = new object();

        private int uiCounter = 0; // Bộ đếm để giảm tốc độ cập nhật giao diện

        private Timer simTimer;
        private Random rand = new Random();
        private bool isSimulating = false;

        public Form1()
        {
            InitializeComponent();
            udpClient = new UdpClient();

            // Gán sự kiện cho các nút bấm
            //this.btn_connect.Click += new EventHandler(this.btn_connect_Click);
            //this.btn_disconnect.Click += new EventHandler(this.btn_disconnect_Click);
            //this.btn_browser.Click += new EventHandler(this.btn_browser_Click);
            //this.btn_csv.Click += new EventHandler(this.btn_csv_Click);
            //this.lb_status.DoubleClick += new EventHandler(this.lb_status_DoubleClick);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Thiết lập giá trị mặc định khi vừa mở app
            txt_path.Text = Path.Combine(Application.StartupPath, "DataLog_STM32.csv");
            lb_status.Text = "Trạng thái kết nối: CHƯA KẾT NỐI";
            lb_status.ForeColor = Color.Gray;
            lbl_volt.Text = "--";
            lbl_tmp.Text = "--";
        }

        // ==========================================
        // 2. KHỐI XỬ LÝ PHẦN CỨNG (USB-HID)
        // ==========================================
        private void btn_connect_Click(object sender, EventArgs e)
        {
            try
            {
                // Đọc VID/PID từ giao diện 
                int vid = Convert.ToInt32(txt_ven.Text, 16);
                int pid = Convert.ToInt32(txt_pro.Text, 16);

                stm32Device = HidDevices.Enumerate(vid, pid).FirstOrDefault();

                if (stm32Device != null)
                {
                    stm32Device.OpenDevice();
                    lb_status.Text = "Trạng thái kết nối: ĐÃ KẾT NỐI";
                    lb_status.ForeColor = Color.Green;

                    // Khóa các ô cấu hình
                    txt_ven.Enabled = false;
                    txt_pro.Enabled = false;
                    btn_connect.Enabled = false;

                    // Bắt đầu đọc dữ liệu ngầm
                    stm32Device.ReadReport(OnReport);
                }
                else
                {
                    MessageBox.Show("Không tìm thấy mạch STM32! Vui lòng kiểm tra lại cáp hoặc VID/PID.", "Lỗi kết nối", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi định dạng VID/PID: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btn_disconnect_Click(object sender, EventArgs e)
        {
            if (stm32Device != null && stm32Device.IsOpen)
            {
                stm32Device.CloseDevice();
                lb_status.Text = "Trạng thái kết nối: ĐÃ NGẮT";
                lb_status.ForeColor = Color.Red;

                // Mở khóa UI
                txt_ven.Enabled = true;
                txt_pro.Enabled = true;
                btn_connect.Enabled = true;

                // Tắt luôn cờ ghi file nếu đang bật
                if (isLogging) StopLogging();
            }
        }

        // ==========================================
        // 3. KHỐI LƯU TRỮ (DATA LOGGER)
        // ==========================================
        private void btn_browser_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "CSV File|*.csv|Text File|*.txt";
            saveFileDialog.Title = "Chọn nơi lưu dữ liệu";
            saveFileDialog.FileName = "DataLog_STM32.csv";

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                txt_path.Text = saveFileDialog.FileName;
            }
        }

        private void btn_csv_Click(object sender, EventArgs e)
        {
            if (!isLogging)
            {
                StartLogging();
            }
            else
            {
                StopLogging();
            }
        }

        private void StartLogging()
        {
            try
            {
                // Mở file 
                logWriter = new StreamWriter(txt_path.Text, true);

                // Nếu file trống không, ghi cái Tiêu đề cột vào trước
                if (new FileInfo(txt_path.Text).Length == 0)
                {
                    logWriter.WriteLine("Time,Voltage(V),Temperature(C)");
                }

                isLogging = true;
                btn_csv.Text = "Đang ghi dữ liệu";
                btn_csv.BackColor = Color.LightCoral;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tạo hoặc mở file log: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopLogging()
        {
            isLogging = false;

            // Khóa luồng lại để đóng file an toàn
            lock (logLock)
            {
                if (logWriter != null)
                {
                    logWriter.Flush(); // Ép đẩy sạch dữ liệu còn kẹt trong bộ đệm ra ổ cứng
                    logWriter.Close(); // Đóng file
                    logWriter = null;
                }
            }

            btn_csv.Text = "Bắt đầu ghi dữ liệu";
            btn_csv.BackColor = Color.LightGreen;
        }

        // ==========================================
        // 4. KHỐI NHẬN DỮ LIỆU & XỬ LÝ
        // ==========================================
        private void OnReport(HidReport report)
        {
            if (!stm32Device.IsOpen) return;

            byte[] rawData = report.Data;

            // 5 byte (4 byte data + 1 byte checksum)
            if (report.ReportId == 0x01 && rawData.Length >= 5)
            {
                // TÍNH CHECKSUM: XOR 4 byte đầu tiên của Payload
                byte calculatedChecksum = (byte)(rawData[0] ^ rawData[1] ^ rawData[2] ^ rawData[3]);

                // Nếu Checksum khớp với byte Checksum 
                if (calculatedChecksum == rawData[4])
                {
                    // BIG-ENDIAN 
                    int rawPot = (rawData[0] << 8) | rawData[1];
                    int rawTemp = (rawData[2] << 8) | rawData[3];

                    // tính toán 
                    float volt = (float)((rawPot / 4095.0) * 3.3);
                    float vSense = (float)((rawTemp / 4095.0) * 3.3);
                    float temp = (vSense - 0.76f) / 0.0025f + 25.0f;

                    // log 
                    if (isLogging)
                    {
                        lock (logLock)
                        {
                            if (logWriter != null)
                            {
                                try
                                {
                                    string logLine = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                                     "{0:HH:mm:ss.fff},{1:F2},{2:F1}", DateTime.Now, volt, temp);
                                    logWriter.WriteLine(logLine);
                                }
                                catch { }
                            }
                        }
                    }

                    // udp 
                    try
                    {
                        string targetIp = txt_IP.GetIP();
                        int targetPort = int.Parse(txt_port.Text);

                        // Nhét lại byte 0x01 vào đầu để Simulink không bị lệch tọa độ
                        byte[] fullPacket = new byte[64];
                        fullPacket[0] = 0x01;
                        Array.Copy(rawData, 0, fullPacket, 1, Math.Min(rawData.Length, 63));

                        udpClient.Send(fullPacket, 64, targetIp, targetPort);
                    }
                    catch { }

                    // CẬP NHẬT GIAO DIỆN (5Hz)
                    uiCounter++;
                    if (uiCounter >= 20)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            lbl_volt.Text = $"{volt:F2}";
                            lbl_tmp.Text = $"{temp:F1}";
                        });
                        uiCounter = 0;
                    }
                }
            }

            // Tiếp tục vòng lặp chờ
            stm32Device.ReadReport(OnReport);
        }

        // ==========================================
        // KHỐI GIẢ LẬP DỮ LIỆU
        // ==========================================
        private void lb_status_DoubleClick(object sender, EventArgs e)
        {
            if (!isSimulating)
            {
                // Khởi tạo Timer chạy tốc độ 10ms (100Hz)
                simTimer = new Timer();
                simTimer.Interval = 10;
                simTimer.Tick += SimTimer_Tick;
                simTimer.Start();

                isSimulating = true;
                lb_status.Text = "Trạng thái: ĐANG GIẢ LẬP (100Hz)";
                lb_status.ForeColor = Color.DarkOrange;

                // Vô hiệu hóa nút kết nối thật
                btn_connect.Enabled = false;
            }
            else
            {
                // Tắt giả lập
                simTimer.Stop();
                simTimer.Dispose();
                isSimulating = false;

                lb_status.Text = "Trạng thái kết nối: CHƯA KẾT NỐI";
                lb_status.ForeColor = Color.Gray;
                btn_connect.Enabled = true;

                lbl_volt.Text = "--";
                lbl_tmp.Text = "--";
            }
        }

        private void SimTimer_Tick(object sender, EventArgs e)
        {
            // 1. Tạo khung Frame chuẩn 64 Bytes rỗng (tất cả là 0x00)
            byte[] mockData = new byte[64];

            // 2. Report ID luôn là 0x01
            mockData[0] = 0x01;

            // 3. Giả lập giá trị biến trở (0 -> 4095)
            // Cho nó dao động ngẫu nhiên quanh mức 2048 (~1.65V)
            int mockPot = 2000 + rand.Next(-50, 50);
            mockData[1] = (byte)((mockPot >> 8) & 0xFF); // High Byte
            mockData[2] = (byte)(mockPot & 0xFF);        // Low Byte

            // 4. Giả lập Nhiệt độ nội (dao động quanh 35 độ C)
            // Công thức ngược: Vsense = (35 - 25)*0.0025 + 0.76 = 0.785V
            // RawTemp = (0.785 / 3.3) * 4095 ≈ 974
            int mockTemp = 974 + rand.Next(-5, 5);
            mockData[3] = (byte)((mockTemp >> 8) & 0xFF); // High Byte
            mockData[4] = (byte)(mockTemp & 0xFF);        // Low Byte

            // 5. Tính toán Checksum chuẩn (XOR từ byte 0 đến byte 4)
            mockData[5] = (byte)(mockData[0] ^ mockData[1] ^ mockData[2] ^ mockData[3] ^ mockData[4]);

            // 6. Đóng gói thành HidReport giả và ném vào hàm xử lý thật
            HidReport fakeReport = new HidReport(64);
            fakeReport.Data = mockData;

            // Gọi trực tiếp hàm OnReport để test toàn bộ logic phía sau
            OnReportMock(fakeReport);
        }

        // Bổ sung một hàm chuyển tiếp để tránh lỗi luồng khi gọi OnReport gốc
        private void OnReportMock(HidReport report)
        {
            // Bỏ qua dòng check stm32Device.IsOpen vì đang giả lập
            byte[] rawData = report.Data;

            if (rawData.Length >= 6 && rawData[0] == 0x01)
            {
                byte calculatedChecksum = (byte)(rawData[0] ^ rawData[1] ^ rawData[2] ^ rawData[3] ^ rawData[4]);

                if (calculatedChecksum == rawData[5])
                {
                    int rawPot = (rawData[1] << 8) | rawData[2];
                    int rawTemp = (rawData[3] << 8) | rawData[4];

                    float volt = (float)((rawPot / 4095.0) * 3.3);
                    float vSense = (float)((rawTemp / 4095.0) * 3.3);
                    float temp = (vSense - 0.76f) / 0.0025f + 25.0f;

                    if (isLogging)
                    {
                        lock (logLock) // Đảm bảo không ai đóng file khi đang ghi
                        {
                            if (logWriter != null)
                            {
                                try
                                {
                                    // Dùng InvariantCulture để ép máy tính luôn dùng dấu chấm (.) cho số thập phân
                                    // Tránh lỗi nát file CSV nếu máy Windows đang cài vùng Việt Nam (dùng dấu phẩy)
                                    string logLine = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                                     "{0:HH:mm:ss.fff},{1:F2},{2:F1}", DateTime.Now, volt, temp);

                                    logWriter.WriteLine(logLine); // Bơm thẳng vào ống xả
                                }
                                catch { /* Bỏ qua lỗi vặt */ }
                            }
                        }
                    }

                    try
                    {
                        string targetIp = txt_IP.GetIP();
                        int targetPort = int.Parse(txt_port.Text);
                        udpClient.Send(rawData, rawData.Length, targetIp, targetPort);
                    }
                    catch { }

                    uiCounter++; // Cứ nhận 1 gói tin thì cộng thêm 1
                    if (uiCounter >= 20) // Đủ 20 gói tin (20 * 10ms = 200ms) thì mới làm mới màn hình 1 lần
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            lbl_volt.Text = $"{volt:F2}";
                            lbl_tmp.Text = $"{temp:F1}";
                        });

                        uiCounter = 0; // Reset bộ đếm về 0 để đếm lại vòng mới
                    }
                }
            }
        }
    }
}