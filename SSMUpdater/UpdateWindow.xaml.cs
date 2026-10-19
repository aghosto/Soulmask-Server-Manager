using System.ComponentModel;
using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace SSMUpdater;

/// <summary>
/// 显示更新阶段和实时进度的窗口。
/// </summary>
public partial class UpdateWindow : Window
{
    private bool _updateFinished;
    private bool _allowClose;
    private UpdateResult? _result;
    private string? _cleanupDirectory;
    private string? _installDirectory;

    public UpdateWindow()
    {
        InitializeComponent();
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ActionButton.IsEnabled = false;
        var progress = new Progress<UpdateProgress>(UpdateProgressChanged);

        try
        {
            _result = await new UpdateService().RunAsync(
                Environment.GetCommandLineArgs().Skip(1).ToArray(),
                progress);
        }
        catch (Exception exception)
        {
            _result = new UpdateResult(false, $"更新程序遇到未处理错误：{exception.Message}");
        }

        _updateFinished = true;
        if (_result?.HandedOff == true)
        {
            _allowClose = true;
            Close();
            return;
        }

        UpdateProgressBar.IsIndeterminate = false;
        if (_result?.Success == true)
        {
            _cleanupDirectory = _result.CleanupDirectory;
            _installDirectory = _result.InstallDirectory;
            UpdateProgressBar.Value = 100;
            StageTextBlock.Text = "更新完成";
            DetailTextBlock.Text = _result.Message;
            PercentTextBlock.Text = "100%";
            ActionButton.Content = "启动管理器";
        }
        else
        {
            StageTextBlock.Text = "更新未完成";
            DetailTextBlock.Text = _result.Message;
            PercentTextBlock.Text = string.Empty;
            ActionButton.Content = "关闭";
        }

        ActionButton.IsEnabled = true;
    }

    private void UpdateProgressChanged(UpdateProgress progress)
    {
        StageTextBlock.Text = progress.Stage;
        DetailTextBlock.Text = progress.Detail;
        UpdateProgressBar.IsIndeterminate = !progress.Percent.HasValue;
        if (progress.Percent.HasValue)
            UpdateProgressBar.Value = Math.Clamp(progress.Percent.Value, 0, 100);

        PercentTextBlock.Text = progress.Percent.HasValue
            ? $"{Math.Clamp(progress.Percent.Value, 0, 100):0}%"
            : string.Empty;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_updateFinished)
            return;

        if (_result?.Success == true)
        {
            try
            {
                string installDir = _installDirectory ?? AppContext.BaseDirectory;
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(installDir, "SoulmaskServerManager.exe"),
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                };
                if (!string.IsNullOrWhiteSpace(_cleanupDirectory))
                {
                    startInfo.ArgumentList.Add("--cleanup-update-dir");
                    startInfo.ArgumentList.Add(_cleanupDirectory);
                    startInfo.ArgumentList.Add("--wait-for-pid");
                    startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                }
                Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                _result = new UpdateResult(false, $"更新已完成，但启动管理器失败：{exception.Message}");
                StageTextBlock.Text = "启动失败";
                DetailTextBlock.Text = _result.Message;
                ActionButton.Content = "关闭";
                return;
            }
        }

        _allowClose = true;
        Close();
    }

    private void UpdateWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_updateFinished && !_allowClose)
            e.Cancel = true;
    }
}
