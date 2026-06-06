using System;
using System.Drawing;
using System.Windows.Forms;

namespace Stm32Gateway // Nhớ đổi tên namespace cho khớp với project của bạn
{
    public class BeautifulIpBox : UserControl
    {
        private TextBox[] box = new TextBox[4];
        private Label[] dot = new Label[3];

        public BeautifulIpBox()
        {
            // Thiết lập khung ngoài (Giả làm 1 cái TextBox bự)
            this.BackColor = Color.White;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(160, 26);
            this.Font = new Font("Consolas", 11F, FontStyle.Regular);

            // Dùng TableLayoutPanel để chia 7 ô đều tăm tắp
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 7;
            layout.RowCount = 1;
            layout.Dock = DockStyle.Fill;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);

            // Chia tỷ lệ: 4 ô TextBox chiếm 25%, 3 ô dấu chấm tự động co giãn
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            for (int i = 0; i < 4; i++)
            {
                box[i] = new TextBox();
                box[i].BorderStyle = BorderStyle.None;
                box[i].MaxLength = 3;
                box[i].Dock = DockStyle.Fill;
                box[i].TextAlign = HorizontalAlignment.Center;
                box[i].Margin = new Padding(0, 2, 0, 0);

                // [TÍNH NĂNG XỊN]: Gõ phím nào bắt phím đó
                box[i].KeyPress += (s, e) =>
                {
                    // Nếu bấm dấu chấm (.), chặn lại và tự động nhảy Tab sang ô kế tiếp
                    if (e.KeyChar == '.' || e.KeyChar == '·')
                    {
                        e.Handled = true;
                        SendKeys.Send("{TAB}");
                    }
                    // Chặn không cho nhập chữ cái, chỉ cho nhập số và phím xóa (Backspace)
                    else if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    {
                        e.Handled = true;
                    }
                };

                // [TÍNH NĂNG XỊN]: Gõ đủ 3 số tự nhảy sang ô kế tiếp
                box[i].TextChanged += (s, e) =>
                {
                    TextBox t = s as TextBox;
                    if (t.Text.Length == 3 && t.SelectionStart == 3)
                    {
                        SendKeys.Send("{TAB}");
                    }
                };

                layout.Controls.Add(box[i], i * 2, 0);

                // Thêm cái dấu chấm nằm lơ lửng ở giữa
                if (i < 3)
                {
                    dot[i] = new Label();
                    dot[i].Text = "·"; // Ký tự đặc biệt: Dấu chấm giữa
                    dot[i].AutoSize = true;
                    dot[i].Dock = DockStyle.Fill;
                    dot[i].TextAlign = ContentAlignment.MiddleCenter;
                    dot[i].Margin = new Padding(0);
                    layout.Controls.Add(dot[i], i * 2 + 1, 0);
                }
            }

            // Gán giá trị mặc định cho giống localhost
            box[0].Text = "127";
            box[1].Text = "0";
            box[2].Text = "0";
            box[3].Text = "1";

            this.Controls.Add(layout);
        }

        // Hàm tiện ích để gọi IP ra xài
        public string GetIP()
        {
            // Tự động nối 4 ô lại thành chuỗi IP hoàn chỉnh
            return $"{box[0].Text}.{box[1].Text}.{box[2].Text}.{box[3].Text}";
        }
    }
}