using System.Windows;

namespace TimeClient
{
    public partial class TimeDisplayWindow : Window
    {
        public TimeDisplayWindow()
        {
            InitializeComponent();
        }

        public void UpdateTime(string timeString)
        {
            TimeDisplayTextBlock.Text = timeString;
        }
    }
}

