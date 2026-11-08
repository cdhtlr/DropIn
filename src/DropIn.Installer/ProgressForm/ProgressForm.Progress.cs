using DropIn.Common;

namespace DropIn.Installer;

sealed partial class ProgressForm
{
    /// <summary>Time constant, in seconds, of the estimated progress curve used while an external installer runs.</summary>
    const double EstimateTimeConstantSeconds = 30;

    /// <summary>Highest percentage an estimate may reach; only a real completion reports 100.</summary>
    const int EstimateCeiling = 95;

    /// <summary>The label that shows the current stage and percentage above the bar.</summary>
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true };

    /// <summary>The timer that advances an estimated percentage while an external installer is running.</summary>
    readonly System.Windows.Forms.Timer _estimate = new() { Interval = 500 };

    /// <summary>The percentage at which the current estimate started.</summary>
    int _estimateFrom;

    /// <summary>The moment the current estimate started.</summary>
    DateTime _estimateStart;

    /// <summary>The stage text shown next to an estimated percentage.</summary>
    string _estimateStage = "";

    /// <summary>Gets a reporter that can be called from any thread and updates the window on the UI thread.</summary>
    public IProgress<InstallProgressInfo> Reporter => new FormReporter(this);

    /// <summary>Forwards progress updates to <see cref="ProgressForm.Apply"/> on the UI thread.</summary>
    sealed class FormReporter(ProgressForm form) : IProgress<InstallProgressInfo>
    {
        /// <summary>Marshals the update to the UI thread when the window handle exists.</summary>
        public void Report(InstallProgressInfo value)
        {
            if (form.IsDisposed || !form.IsHandleCreated) return;
            try { form.BeginInvoke(() => form.Apply(value)); }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
        }
    }

    /// <summary>Wires the estimate timer; called once while the window is built.</summary>
    void InitializeProgress()
    {
        _estimate.Tick += (_, _) => ShowEstimate();
    }

    /// <summary>Shows a measured percentage, or starts estimating one when the work cannot be measured.</summary>
    void Apply(InstallProgressInfo info)
    {
        if (_allowClose && _result.Visible) return; // the final result is already shown
        _bar.Style = ProgressBarStyle.Continuous;
        if (info.Estimated)
        {
            if (!_estimate.Enabled)
            {
                _estimateFrom = info.Percent;
                _estimateStart = DateTime.UtcNow;
                _estimateStage = info.Stage;
                _bar.Value = Math.Clamp(info.Percent, 0, 100);
                _estimate.Start();
            }
            _status.Text = $"{info.Stage}… {_bar.Value}% (estimated)";
            return;
        }
        _estimate.Stop();
        _bar.Value = Math.Clamp(info.Percent, 0, 100);
        _status.Text = $"{info.Stage}… {_bar.Value}%";
    }

    /// <summary>Advances the estimated percentage along an exponential curve that never passes the ceiling.</summary>
    void ShowEstimate()
    {
        var elapsed = (DateTime.UtcNow - _estimateStart).TotalSeconds;
        var fraction = 1 - Math.Exp(-elapsed / EstimateTimeConstantSeconds);
        var percent = _estimateFrom + (int)((EstimateCeiling - _estimateFrom) * fraction);
        _bar.Value = Math.Clamp(percent, 0, EstimateCeiling);
        _status.Text = $"{_estimateStage}… {_bar.Value}% (estimated)";
    }

    /// <summary>Shows the final state on the bar once the installation ended.</summary>
    void CompleteProgress(bool success)
    {
        _estimate.Stop();
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = success ? 100 : 0;
        _status.Text = success ? "Completed 100%" : "Failed";
    }
}
