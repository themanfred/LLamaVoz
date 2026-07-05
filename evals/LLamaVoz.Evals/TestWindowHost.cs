namespace LLamaVoz.Evals;

/// <summary>
/// Hosts a WinForms window with a TextBox on its own STA thread — insertion evals only
/// ever type into windows owned by this process, never into user applications.
/// Ported from poc/LLamaVoz.Poc.Insertion.
/// </summary>
public sealed class TestWindowHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private System.Windows.Forms.Form? _form;
    private System.Windows.Forms.TextBox? _textBox;
    private Thread? _thread;

    public IntPtr WindowHandle { get; private set; }

    public static TestWindowHost Start(bool passwordBox = false)
    {
        var host = new TestWindowHost();
        host._thread = new Thread(() => host.RunMessageLoop(passwordBox)) { IsBackground = true };
        host._thread.SetApartmentState(ApartmentState.STA);
        host._thread.Start();
        return host;
    }

    private void RunMessageLoop(bool passwordBox)
    {
        _form = new System.Windows.Forms.Form
        {
            Text = "LLamaVoz Evals — ventana de prueba",
            Width = 520,
            Height = 160,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        };
        _textBox = new System.Windows.Forms.TextBox
        {
            Multiline = !passwordBox,
            UseSystemPasswordChar = passwordBox,
            Dock = System.Windows.Forms.DockStyle.Fill,
        };
        _form.Controls.Add(_textBox);
        _form.Shown += (_, _) =>
        {
            WindowHandle = _form.Handle;
            _textBox.Focus();
            _ready.Set();
        };
        System.Windows.Forms.Application.Run(_form);
    }

    public bool WaitReady(TimeSpan timeout) => _ready.Wait(timeout);

    public string GetTextBoxText()
    {
        if (_form is null || _textBox is null)
        {
            return string.Empty;
        }
        return (string)_form.Invoke(() => _textBox.Text);
    }

    public void Dispose()
    {
        try
        {
            if (_form is not null && _form.IsHandleCreated)
            {
                _form.Invoke(() => _form.Close());
            }
            _thread?.Join(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Best-effort teardown for a test window.
        }
        _ready.Dispose();
    }
}
