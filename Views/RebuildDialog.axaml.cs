using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RAID_Util.Views
{
    public partial class RebuildDialog : Window
    {
        private readonly string _arrayName;
        private readonly DispatcherTimer _timer;

        public RebuildDialog(string arrayName)
        {
            InitializeComponent();

            _arrayName = arrayName;
            TitleText.Text = $"Rebuilding {arrayName}";

            CloseButton.Click += (_, __) => Close();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, __) => UpdateStatus();
            _timer.Start();

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (!File.Exists("/proc/mdstat"))
                return;

            var text = File.ReadAllText("/proc/mdstat");

            if (!text.Contains(_arrayName))
                return;

            var match = Regex.Match(text,
                @"resync\s*=\s*(?<percent>[\d\.]+)%.*?\((?<done>\d+)\/(?<total>\d+)\).*?finish=(?<eta>[\d\.]+)min.*?speed=(?<speed>\d+)K/sec",
                RegexOptions.Singleline);

            if (!match.Success)
                return;

            double percent = double.Parse(match.Groups["percent"].Value);
            long done = long.Parse(match.Groups["done"].Value);
            long total = long.Parse(match.Groups["total"].Value);
            double eta = double.Parse(match.Groups["eta"].Value);
            long speedKB = long.Parse(match.Groups["speed"].Value);

            ProgressBar.Value = percent;
            PercentText.Text = $"{percent:F1}%  ({done:N0} / {total:N0} blocks)";
            SpeedText.Text = $"Speed: {speedKB / 1024.0:F1} MB/s";
            EtaText.Text = $"ETA: {eta:F1} min";

            if (percent >= 100)
            {
                _timer.Stop();
                Close();
            }
        }
    }
}
