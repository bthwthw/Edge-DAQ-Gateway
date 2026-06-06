using System.Drawing;
using System.Windows.Forms;

namespace DLMT_BTL 
{
    public class BeautifulGroupBox : GroupBox
    {
        // Tạo một thuộc tính mới để bạn có thể đổi màu viền tùy thích
        private Color borderColor = Color.DodgerBlue; // Đổi màu mặc định ở đây

        public Color BorderColor
        {
            get { return borderColor; }
            set { borderColor = value; this.Invalidate(); }
        }

        // Ép C# cho phép mình tự vẽ lại Control này
        protected override void OnPaint(PaintEventArgs e)
        {
            // Lấy kích thước chữ của tiêu đề
            Size tSize = TextRenderer.MeasureText(this.Text, this.Font);
            Rectangle borderRect = e.ClipRectangle;
            borderRect.Y += tSize.Height / 2;
            borderRect.Height -= tSize.Height / 2;

            // 1. Vẽ cái viền mới với màu của mình
            ControlPaint.DrawBorder(e.Graphics, borderRect, this.borderColor, ButtonBorderStyle.Solid);

            // 2. Tạo một khoảng trống trên viền
            Rectangle textRect = e.ClipRectangle;
            textRect.X += 8; // Thụt lề chữ vào 8 pixel
            textRect.Width = tSize.Width;
            textRect.Height = tSize.Height;
            e.Graphics.FillRectangle(new SolidBrush(this.BackColor), textRect);

            // 3. In cái chữ Tiêu đề lên
            e.Graphics.DrawString(this.Text, this.Font, new SolidBrush(this.ForeColor), textRect);
        }
    }
}