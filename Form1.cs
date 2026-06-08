using System;
using System.Collections.Concurrent; // Thư viện cho ConcurrentQueue
using System.Diagnostics;          // Thư viện cho Stopwatch
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;       // Thư viện cho Task chạy ngầm
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

        private bool isUdpError = false; // theo dõi trạng thái mạng 

        // KIẾN TRÚC PRODUCER-CONSUMER CHO DATA LOGGER
        private bool isLogging = false;
        private StreamWriter logWriter;
        private readonly object logLock = new object();
        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>(); // Hàng đợi an toàn đa luồng

        // CƠ CHẾ TIME-BASED UI REFRESH (5Hz)
        private readonly Stopwatch uiStopwatch = new Stopwatch();

        // GIẢ LẬP DỮ LIỆU
        private Timer simTimer;
        private readonly Random rand = new Random();
        private bool isSimulating = false;

        public Form1()
        {
            InitializeComponent();
            udpClient = new UdpClient();

            // Khởi động Luồng tiêu thụ ngầm (Consumer Task) chạy vĩnh viễn suốt vòng đời ứng dụng
            Task.Run(() => BackgroundLogProcessorAsync());
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Thiết lập giá trị mặc định lúc load ứng dụng
            txt_path.Text = Path.Combine(Application.StartupPath, "DataLog_STM32.csv");
            lb_status.Text = "Trạng thái kết nối: CHƯA KẾT NỐI";
            lb_status.ForeColor = Color.Gray;
            lbl_volt.Text = "--";
            lbl_tmp.Text = "--";

            // Kích hoạt đồng hồ bấm giờ đo Delta Time cho giao diện
            uiStopwatch.Start();
        }

        // ==========================================
        // 2. KHỐI XỬ LÝ PHẦN CỨNG (USB-HID)
        // ==========================================
        private void btn_connect_Click(object sender, EventArgs e)
        {
            try
            {
                int vid = Convert.ToInt32(txt_ven.Text, 16);
                int pid = Convert.ToInt32(txt_pro.Text, 16);

                stm32Device = HidDevices.Enumerate(vid, pid).FirstOrDefault();

                if (stm32Device != null)
                {
                    stm32Device.OpenDevice();
                    lb_status.Text = "Trạng thái kết nối: ĐÃ KẾT NỐI";
                    lb_status.ForeColor = Color.Green;

                    txt_ven.Enabled = false;
                    txt_pro.Enabled = false;
                    btn_connect.Enabled = false;

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

                txt_ven.Enabled = true;
                txt_pro.Enabled = true;
                btn_connect.Enabled = true;

                if (isLogging) StopLogging();
            }
        }

        // ==========================================
        // 3. KHỐI LƯU TRỮ (PRODUCER - CONSUMER PATTERN)
        // ==========================================
        private void btn_browser_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File|*.csv|Text File|*.txt",
                Title = "Chọn nơi lưu dữ liệu",
                FileName = "DataLog_STM32.csv"
            };

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                txt_path.Text = saveFileDialog.FileName;
            }
        }

        private void btn_csv_Click(object sender, EventArgs e)
        {
            if (!isLogging) StartLogging();
            else StopLogging();
        }

        private void StartLogging()
        {
            try
            {
                lock (logLock)
                {
                    logWriter = new StreamWriter(txt_path.Text, true);
                    if (new FileInfo(txt_path.Text).Length == 0)
                    {
                        logWriter.WriteLine("Time,Voltage(V),Temperature(C)");
                        logWriter.Flush();
                    }
                }
                isLogging = true;
                btn_csv.Text = "Đang ghi dữ liệu (Nhấn để dừng)";
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

            // Xả sạch dữ liệu tồn đọng trong Queue xuống ổ đĩa trước khi đóng file
            lock (logLock)
            {
                if (logWriter != null)
                {
                    while (logQueue.TryDequeue(out string remainingLine))
                    {
                        logWriter.WriteLine(remainingLine);
                    }
                    logWriter.Flush();
                    logWriter.Close();
                    logWriter = null;
                }
            }

            btn_csv.Text = "Bắt đầu ghi dữ liệu";
            btn_csv.BackColor = Color.LightGreen;
        }

        // LUỒNG CONSUMER CHẠY NGẦM: Liên tục tiêu thụ dữ liệu từ Queue xả xuống ổ cứng
        private async Task BackgroundLogProcessorAsync()
        {
            while (!this.IsDisposed) // Chạy xuyên suốt đến khi tắt App
            {
                if (isLogging && !logQueue.IsEmpty)
                {
                    lock (logLock)
                    {
                        if (logWriter != null)
                        {
                            // Dequeue sạch bách hàng đợi trong bộ nhớ RAM ở chu kỳ này
                            while (logQueue.TryDequeue(out string logLine))
                            {
                                logWriter.WriteLine(logLine);
                            }
                            logWriter.Flush(); // Ép ổ cứng lưu vật lý hàng loạt (Batch Write)
                        }
                    }
                }
                // Nghỉ ngơi 20ms để nhường nhịp CPU cho tác vụ khác, tránh hiện tượng gửi lặp (Spam)
                await Task.Delay(20);
            }
        }

        // ==========================================
        // 4. KHỐI NHẬN DỮ LIỆU & XỬ LÝ (CORE LOGIC)
        // ==========================================
        private void OnReport(HidReport report)
        {
            if (!stm32Device.IsOpen) return;

            byte[] rawData = report.Data;

            // Kiểm tra cấu trúc mảng tối thiểu 5 byte (Bỏ qua kiểm tra ID cứng nhắc để tăng độ tương thích)
            if (rawData.Length >= 5)
            {
                byte calculatedChecksum = (byte)(rawData[0] ^ rawData[1] ^ rawData[2] ^ rawData[3]);

                if (calculatedChecksum == rawData[4])
                {
                    // GIẢI MÃ SÁT LỀ TRÁI (Đã đồng bộ với mạch thật)
                    int rawPot = (rawData[0] << 8) | rawData[1];
                    int rawTemp = (rawData[2] << 8) | rawData[3];

                    float volt = (float)((rawPot / 4095.0) * 3.3);
                    float vSense = (float)((rawTemp / 4095.0) * 3.3);
                    float temp = (vSense - 0.76f) / 0.0025f + 25.0f;

                    // TÁC VỤ 1: PRODUCER - Chỉ ném chuỗi vào Queue rồi té ngay lập tức, tốn 0.0001ms
                    if (isLogging)
                    {
                        string logLine = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         "{0:HH:mm:ss.fff},{1:F2},{2:F1}", DateTime.Now, volt, temp);
                        logQueue.Enqueue(logLine); // Không bao giờ sợ nghẽn luồng USB
                    }

                    // TÁC VỤ 2: TRUYỀN UDP ĐẾN SIMULINK (Tốc độ tối đa 100Hz)
                    try
                    {
                        string targetIp = txt_IP.GetIP();
                        int targetPort = int.Parse(txt_port.Text);

                        byte[] fullPacket = new byte[64];
                        fullPacket[0] = 0x01; // Phục hồi Report ID cho Simulink giải mã
                        Array.Copy(rawData, 0, fullPacket, 1, Math.Min(rawData.Length, 63));

                        udpClient.Send(fullPacket, 64, targetIp, targetPort);
                    }
                    catch 
                    {
                        isUdpError = true;
                    }

                    // TÁC VỤ 3: TIME-BASED UI REFRESH (Chuẩn định thời 5Hz thực tế)
                    if (uiStopwatch.ElapsedMilliseconds >= 200) // Đúng 200ms thực tế trôi qua mới làm tươi UI
                    {
                        uiStopwatch.Restart(); // Reset đồng hồ bấm giờ về 0

                        this.Invoke((MethodInvoker)delegate
                        {
                            lbl_volt.Text = $"{volt:F2}";
                            lbl_tmp.Text = $"{temp:F1}";

                            if (isUdpError)
                            {
                                lb_status.Text = "CẢNH BÁO: Lỗi truyền tải UDP!";
                                lb_status.ForeColor = Color.Red;
                            }
                            else if (stm32Device.IsOpen && !isSimulating)
                            {
                                lb_status.Text = "Trạng thái kết nối: ĐÃ KẾT NỐI";
                                lb_status.ForeColor = Color.Green;
                            }
                        });
                    }
                }
            }

            // Tiếp tục vòng lặp hứng ngắt USB
            stm32Device.ReadReport(OnReport);
        }

        // ==========================================
        // 5. KHỐI GIẢ LẬP DỮ LIỆU (SIMULATION MODE)
        // ==========================================
        private void lb_status_DoubleClick(object sender, EventArgs e)
        {
            if (!isSimulating)
            {
                simTimer = new Timer { Interval = 10 };
                simTimer.Tick += SimTimer_Tick;
                simTimer.Start();

                isSimulating = true;
                lb_status.Text = "Trạng thái: ĐANG GIẢ LẬP (100Hz)";
                lb_status.ForeColor = Color.DarkOrange;
                btn_connect.Enabled = false;
            }
            else
            {
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
            byte[] mockData = new byte[64];

            // Giả lập Payload bị xê dịch trái giống y phần cứng thật (Mất byte 0x01 ở đầu)
            int mockPot = 2000 + rand.Next(-50, 50);
            mockData[0] = (byte)((mockPot >> 8) & 0xFF);
            mockData[1] = (byte)(mockPot & 0xFF);

            int mockTemp = 974 + rand.Next(-5, 5);
            mockData[2] = (byte)((mockTemp >> 8) & 0xFF);
            mockData[3] = (byte)(mockTemp & 0xFF);

            mockData[4] = (byte)(mockData[0] ^ mockData[1] ^ mockData[2] ^ mockData[3]);

            HidReport fakeReport = new HidReport(64) { Data = mockData };
            OnReportMock(fakeReport);
        }

        private void OnReportMock(HidReport report)
        {
            // Hàm chuyển tiếp giả lập chạy y chang hàm OnReport thật ở trên
            byte[] rawData = report.Data;

            if (rawData.Length >= 5)
            {
                byte calculatedChecksum = (byte)(rawData[0] ^ rawData[1] ^ rawData[2] ^ rawData[3]);

                if (calculatedChecksum == rawData[4])
                {
                    int rawPot = (rawData[0] << 8) | rawData[1];
                    int rawTemp = (rawData[2] << 8) | rawData[3];

                    float volt = (float)((rawPot / 4095.0) * 3.3);
                    float vSense = (float)((rawTemp / 4095.0) * 3.3);
                    float temp = (vSense - 0.76f) / 0.0025f + 25.0f;

                    if (isLogging)
                    {
                        string logLine = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         "{0:HH:mm:ss.fff},{1:F2},{2:F1}", DateTime.Now, volt, temp);
                        logQueue.Enqueue(logLine);
                    }

                    try
                    {
                        string targetIp = txt_IP.GetIP();
                        int targetPort = int.Parse(txt_port.Text);
                        byte[] fullPacket = new byte[64];
                        fullPacket[0] = 0x01;
                        Array.Copy(rawData, 0, fullPacket, 1, Math.Min(rawData.Length, 63));
                        udpClient.Send(fullPacket, 64, targetIp, targetPort);
                    }
                    catch 
                    {   isUdpError = true; 
                    }

                    // Thực thi Time-based refresh cho chế độ giả lập luôn
                    if (uiStopwatch.ElapsedMilliseconds >= 200)
                    {
                        uiStopwatch.Restart();
                        this.Invoke((MethodInvoker)delegate
                        {
                            lbl_volt.Text = $"{volt:F2}";
                            lbl_tmp.Text = $"{temp:F1}";

                            if (isUdpError)
                            {
                                lb_status.Text = "CẢNH BÁO: Lỗi truyền tải UDP!";
                                lb_status.ForeColor = Color.Red;
                            }
                            else
                            {
                                lb_status.Text = "Trạng thái: ĐANG GIẢ LẬP";
                                lb_status.ForeColor = Color.DarkOrange;
                            }
                        });
                    }
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isLogging)
            {
                StopLogging();
            }
        }
    }
}