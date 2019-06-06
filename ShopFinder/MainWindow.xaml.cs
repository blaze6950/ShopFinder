using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace ShopFinder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public ObservableCollection<Shop> ObservableCollection;
        public Processor Processor { get; set; }
        private bool _isStarted = false;
        public Stopwatch Stopwatch { get; set; }

        private DispatcherTimer _dispatcherTimer;

        public MainWindow()
        {
            InitializeComponent();
            ObservableCollection = new ObservableCollection<Shop>();
            Processor = new Processor(ObservableCollection);
            BindingOperations.EnableCollectionSynchronization(ObservableCollection, Processor);
            CategoryComboBox.ItemsSource = Processor.Categories.Keys;
            SortingComboBox.ItemsSource = Processor.Sortings.Keys;
            ResultListView.ItemsSource = ObservableCollection;
            Stopwatch = new Stopwatch();
            _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Tick += DispatchTimerTick;
            _dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 1);
        }

        private async void FindButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isStarted)
                {
                    await Start();
                }
                else
                {
                    Stop();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Ooops");
            }
        }

        private async Task Start()
        {
            Stopwatch.Start();
            _dispatcherTimer.Start();
            int maxPages = int.Parse(MaxPagesTextBox.Text);
            int maxSites = int.Parse(MaxSitesTextBox.Text);
            int delay = int.Parse(DelayTextBox.Text);
            var selectedCategory = CategoryComboBox.SelectedItem as string;
            var selectedSort = SortingComboBox.SelectedItem as string;

            Processor.Category = Processor.Categories[selectedCategory];
            Processor.Sorting = Processor.Sortings[selectedSort];
            Processor.Delay = delay;
            Processor.MaxSites = maxSites;
            Processor.MaxPages = maxPages;

            FindButtonText.Content = "STOP";

            await Processor.StartParcing();
            _isStarted = !_isStarted;
        }
        private void Stop()
        {
            Stopwatch.Stop();
            Processor.StopParcing();
            FindButtonText.Content = "Find!";
            _isStarted = !_isStarted;
        }

        public void Dispose()
        {
            Processor?.Dispose();
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            if (_isStarted)
            {
                Stop();
            }
            Dispose();
        }

        private void DispatchTimerTick(object sender, EventArgs e)
        {
            DeadLabel.Content = Processor.Dead;
            ProcessedLabel.Content = Processor.Shops.Count;
            TimeLabel.Content = Stopwatch.Elapsed;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStarted)
            {
                Stop();
            }
            var result = MessageBox.Show("If you confirm this operation will be cleared the list of sites and state will be refreshed.", "Are you sure?", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                ObservableCollection = new ObservableCollection<Shop>();
                Processor = new Processor(ObservableCollection);
                BindingOperations.EnableCollectionSynchronization(ObservableCollection, Processor);
                CategoryComboBox.ItemsSource = Processor.Categories.Keys;
                SortingComboBox.ItemsSource = Processor.Sortings.Keys;
                ResultListView.ItemsSource = ObservableCollection;
            }
        }

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = sender as ListViewItem;
            if (item != null && item.IsSelected)
            {
                var itemValue = item.Content.ToString();
                Clipboard.SetText(itemValue);
            }
        }
    }
}
