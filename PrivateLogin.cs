using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PadLauncher;

public record PrivateLoginAccount(string Account, bool PasswordSaved);
public interface IPrivateLoginStore
{
    PrivateLoginAccount? Read(string character);
    void Save(string character, string account, string password);
    void Forget(string character);
}

// Secrets stay in the current Windows user credential store and out of Config and diagnostics.
public sealed class WindowsPrivateLoginStore : IPrivateLoginStore
{
    static string Target(string character) => "Offhand.PrivateLogin/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(character.Trim().ToUpperInvariant())));
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
    static void Clear(IntPtr buffer, uint length) { for (uint i = 0; i < length; i++) Marshal.WriteByte(buffer, (int)i, 0); }
    public PrivateLoginAccount? Read(string character)
    {
        if (!CredReadW(Target(character), 1, 0, out var ptr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error);
        }
        try
        {
            var entry = Marshal.PtrToStructure<Credential>(ptr);
            try { return new(entry.UserName ?? "", entry.CredentialBlobSize > 0); }
            finally { Clear(entry.CredentialBlob, entry.CredentialBlobSize); }
        }
        finally { CredFree(ptr); }
    }
    public PrivatePolInput.Stroke[]? PasswordPlan(string character, IntPtr window)
    {
        if (!CredReadW(Target(character), 1, 0, out var ptr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error);
        }
        try
        {
            var entry = Marshal.PtrToStructure<Credential>(ptr);
            try
            {
                if (!string.Equals(entry.UserName, character, StringComparison.OrdinalIgnoreCase) ||
                    entry.CredentialBlobSize == 0 || entry.CredentialBlobSize > 2560 ||
                    entry.CredentialBlobSize % 2 != 0) return null;
                var chars = new char[entry.CredentialBlobSize / 2];
                try
                {
                    Marshal.Copy(entry.CredentialBlob, chars, 0, chars.Length);
                    return PrivatePolInput.WindowPlan(window, chars);
                }
                finally { Array.Clear(chars); }
            }
            finally { Clear(entry.CredentialBlob, entry.CredentialBlobSize); }
        }
        finally { CredFree(ptr); }
    }

    public void Save(string character, string account, string password)
    {
        if (string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(account) || password.Length == 0) throw new ArgumentException("Choose a character, account label and password.");
        var bytes = Encoding.Unicode.GetBytes(password);
        try
        {
            if (bytes.Length > 2560) throw new ArgumentException("Password is too long for Windows Credential Manager.");
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var entry = new Credential { Type = 1, TargetName = Target(character), UserName = account.Trim(), CredentialBlob = blob, CredentialBlobSize = (uint)bytes.Length, Persist = 2 };
                if (!CredWriteW(ref entry, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { Clear(blob, (uint)bytes.Length); Marshal.FreeHGlobal(blob); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Forget(string character)
    {
        if (!CredDeleteW(Target(character), 1, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new Win32Exception(error);
        }
    }
}

public sealed class PrivateLoginForm : Form
{
    readonly IPrivateLoginStore store;
    readonly ComboBox character = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly TextBox account = new() { Dock = DockStyle.Fill, MaxLength = 200 };
    readonly TextBox password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 1280 };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(490, 0) };
    readonly Button save = new() { Text = "Save password", AutoSize = true, Enabled = false };
    readonly Button forget = new() { Text = "Forget password", AutoSize = true, Enabled = false };
    bool loading;
    public PrivateLoginForm(IEnumerable<string> characters, IPrivateLoginStore? store = null)
    {
        this.store = store ?? new WindowsPrivateLoginStore();
        Text = "Private login settings"; Width = 560; Height = 550; MinimumSize = new Size(520, 550);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control c) { c.Margin = new Padding(0, 0, 0, 10); layout.Controls.Add(c); }
        Row(new Label { Text = "Optional PlayOnline sign-in. Save a password and teach a path for each character you want to sign in.", AutoSize = true, MaximumSize = new Size(490, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });
        Row(new Label { Text = "Character", AutoSize = true }); Row(character);
        Row(new Label { Text = "PlayOnline member label (must match the Offhand character label)", AutoSize = true }); Row(account);
        Row(new Label { Text = "Password (type a new one to save or replace)", AutoSize = true }); Row(password);
        Row(status);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange(new Control[] { save, forget, close }); Row(actions);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout); CancelButton = close;
        character.SelectedIndexChanged += (_, _) => LoadCharacter();
        account.TextChanged += (_, _) => UpdateSave(); password.TextChanged += (_, _) => UpdateSave();
        save.Click += (_, _) => SavePassword(); forget.Click += (_, _) => ForgetPassword();
        character.Items.AddRange(characters.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        if (character.Items.Count > 0) character.SelectedIndex = 0;
        else { account.Enabled = password.Enabled = false; status.Text = "Add a character first, then return here."; }
        FormClosed += (_, _) => password.Clear();
    }
    string Selected => character.SelectedItem as string ?? "";
    void UpdateSave() { if (!loading) save.Enabled = Selected.Length > 0 && account.Text.Trim().Length > 0 && password.Text.Length > 0; }
    void LoadCharacter()
    {
        loading = true; password.Clear(); save.Enabled = forget.Enabled = false; account.Clear(); account.Enabled = password.Enabled = true;
        try
        {
            var saved = store.Read(Selected); account.Text = saved?.Account ?? "";
            forget.Enabled = saved != null;
            save.Text = saved == null ? "Save password" : "Replace password";
            status.Text = (saved?.PasswordSaved == true ? "Password saved in Windows Credential Manager." : "No password saved.") + "\nKept outside exports and diagnostics. Stored per character label; forget it before renaming or removing that character.";
        }
        catch { account.Enabled = password.Enabled = false; status.Text = "Windows credential storage could not be opened. Nothing was changed."; }
        finally { loading = false; UpdateSave(); }
    }
    void SavePassword()
    {
        if (!save.Enabled) return;
        try { store.Save(Selected, account.Text, password.Text); LoadCharacter(); }
        catch { password.Clear(); status.Text = "Password could not be saved. Enter it again to retry. Nothing was sent to POL."; }
    }
    void ForgetPassword()
    {
        if (MessageBox.Show(this, "Remove the locally saved password for this character? This does not change the account password.", "Forget password", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { store.Forget(Selected); LoadCharacter(); }
        catch { status.Text = "Password could not be removed. Try again or use Windows Credential Manager."; }
    }
}

// Private diagnostic only: sends one Backspace to an Offhand-owned waiting POL client.
public static class PrivatePolInput
{
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputData { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputData Data; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

    public const string PasswordSent = "Saved password input sent. Verify the field, then continue manually.";
    public const string DummyText = "Aa09!@#_-+=?";
    public readonly record struct Stroke(ushort Scan, bool Extended, bool Up);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScanExW(char character, IntPtr layout);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint key, uint kind, IntPtr layout);
    public static Stroke[] Plan(ReadOnlySpan<char> text, Func<char, (ushort Scan, bool Extended, int Modifiers)?> map)
    {
        var result = new List<Stroke>();
        foreach (char c in text)
        {
            if (char.IsControl(c)) throw new ArgumentException("Control characters are not supported.");
            var mapped = map(c) ?? throw new ArgumentException("Character cannot be mapped with the target keyboard layout.");
            if (mapped.Scan == 0 || (mapped.Modifiers & ~7) != 0) throw new ArgumentException("Unsupported keyboard mapping.");
            var modifiers = new List<Stroke>();
            if ((mapped.Modifiers & 2) != 0) modifiers.Add(new(0x1d, false, false));
            if ((mapped.Modifiers & 4) != 0) modifiers.Add(new(0x38, false, false));
            if ((mapped.Modifiers & 1) != 0) modifiers.Add(new(0x2a, false, false));
            result.AddRange(modifiers);
            result.Add(new(mapped.Scan, mapped.Extended, false));
            result.Add(new(mapped.Scan, mapped.Extended, true));
            result.AddRange(modifiers.AsEnumerable().Reverse().Select(k => k with { Up = true }));
        }
        return result.ToArray();
    }
    public static Stroke[] DummyPlan(IntPtr window) => WindowPlan(window, DummyText);
    public static Stroke[] WindowPlan(IntPtr window, ReadOnlySpan<char> text)
    {
        uint thread = GetWindowThreadProcessId(window, out _);
        IntPtr layout = GetKeyboardLayout(thread);
        return Plan(text, c => {
            short mapped = VkKeyScanExW(c, layout);
            if (mapped == -1) return null;
            uint scan = MapVirtualKeyExW((uint)(mapped & 255), 4, layout);
            return ((ushort)(scan & 255), (scan & 0xff00) == 0xe000, (mapped >> 8) & 255);
        });
    }
    static uint SendStroke(Stroke stroke)
    {
        var input = new Input { Type = 1, Data = new InputData { Key = new KeyInput { Scan = stroke.Scan,
            Flags = 8u | (stroke.Up ? 2u : 0) | (stroke.Extended ? 1u : 0) } } };
        return SendInput(1, new[] { input }, InputSize);
    }
    public static async Task<string> TypePlan(Stroke[] plan, Func<bool> valid, Func<bool> foreground,
        Func<bool> keysReleased, CancellationToken cancellation, Func<Stroke, uint>? send = null,
        Func<CancellationToken, Task>? pause = null, bool savedPassword = false)
    {
        if (!valid() || !foreground() || !keysReleased()) return "Stopped: target, foreground or held-key check failed. No input sent.";
        send ??= SendStroke; pause ??= token => Task.Delay(45, token);
        var held = new List<Stroke>(); int accepted = 0;
        try
        {
            foreach (var stroke in plan)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!valid() || !foreground()) return "Stopped: target or focus changed. Check the dummy field before retrying.";
                // Track before sending: even an exceptional native boundary must attempt release.
                if (!stroke.Up) held.Add(stroke);
                if (send(stroke) != 1) return "Stopped: Windows rejected input. Check the dummy field before retrying.";
                accepted++;
                if (stroke.Up) held.RemoveAll(k => k.Scan == stroke.Scan && k.Extended == stroke.Extended);
                await pause(cancellation);
            }
            return savedPassword
                ? PasswordSent
                : $"Windows accepted {accepted}/{plan.Length} dummy typing events. Verify the field; no login submitted.";
        }
        finally
        {
            foreach (var key in held.AsEnumerable().Reverse()) send(key with { Up = true });
        }
    }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rectangle rect);

    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    public sealed record Anchor(int X, int Y, int Width, int Height, string? Signature);

    public static Anchor? ReadAnchor(IntPtr window)
    {
        if (!GetCursorPos(out var pointer) || !GetClientRect(window, out var client)) return null;
        var point = pointer;
        if (!ScreenToClient(window, ref point)) return null;
        const int width = 144, height = 44;
        var patch = new Rectangle(point.X - width / 2, point.Y - height / 2, width, height);
        if (!client.Contains(patch)) return null;
        var origin = patch.Location;
        if (!ClientToScreen(window, ref origin)) return null;
        foreach (var sample in new[] { pointer, origin, new Point(origin.X + width - 1, origin.Y),
            new Point(origin.X, origin.Y + height - 1), new Point(origin.X + width - 1, origin.Y + height - 1) })
            if (GetAncestor(WindowFromPoint(sample), 2) != window) return null;

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(origin, Point.Empty, bitmap.Size);
        const int cell = 4;
        var samples = new byte[width / cell * (height / cell)];
        int i = 0;
        for (int y = 0; y < height; y += cell)
            for (int x = 0; x < width; x += cell)
            {
                int sum = 0;
                for (int dy = 0; dy < cell; dy++)
                    for (int dx = 0; dx < cell; dx++)
                    {
                        var color = bitmap.GetPixel(x + dx, y + dy);
                        sum += 3 * color.R + 6 * color.G + color.B;
                    }
                samples[i++] = (byte)(sum / (10 * cell * cell));
            }
        return new(point.X, point.Y, client.Width, client.Height, Convert.ToBase64String(samples));
    }

    public static bool HasAnchorDetail(Anchor anchor)
    {
        if (anchor.Signature == null) return false;
        var cells = Convert.FromBase64String(anchor.Signature);
        if (cells.Length != 396) return false;
        int edges = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (i % 36 != 0 && Math.Abs(cells[i] - cells[i - 1]) >= 24) edges++;
            if (i >= 36 && Math.Abs(cells[i] - cells[i - 36]) >= 24) edges++;
        }
        return edges >= 18;
    }

    public static bool MatchesAnchor(Anchor expected, Anchor actual)
    {
        if (expected.X != actual.X || expected.Y != actual.Y ||
            expected.Width != actual.Width || expected.Height != actual.Height ||
            !HasAnchorDetail(expected) || actual.Signature == null) return false;
        var taught = Convert.FromBase64String(expected.Signature!);
        var observed = Convert.FromBase64String(actual.Signature);
        if (observed.Length != taught.Length) return false;
        int difference = 0, changed = 0;
        for (int i = 0; i < taught.Length; i++)
        {
            int delta = Math.Abs(taught[i] - observed[i]);
            difference += delta;
            if (delta > 24) changed++;
        }
        return difference <= taught.Length * 9 && changed <= taught.Length / 10;
    }

    public static bool MoveToAnchor(IntPtr window, Anchor anchor)
    {
        if (!GetClientRect(window, out var client) || client.Width != anchor.Width || client.Height != anchor.Height)
            return false;
        var point = new Point(anchor.X, anchor.Y);
        return client.Contains(point) && ClientToScreen(window, ref point) && SetCursorPos(point.X, point.Y);
    }

    public static string ClickAtPointer(IntPtr window, Func<bool> valid, Anchor? expected = null)
    {
        if (!valid()) return "Stopped: the tracked POL client changed or loaded FFXI. No click sent.";
        if (!KeysReleased() || (GetAsyncKeyState(1) & 0x8000) != 0)
            return "Stopped: a mouse button, Backspace or modifier key is held. No click sent.";
        if (!GetCursorPos(out var point) || GetAncestor(WindowFromPoint(point), 2) != window ||
            !ScreenToClient(window, ref point) || !GetClientRect(window, out var client) || !client.Contains(point))
            return "Stopped: the pointer is outside the visible POL content or another window covers it. No click sent.";
        if (expected != null && (point.X != expected.X || point.Y != expected.Y ||
            client.Width != expected.Width || client.Height != expected.Height))
            return "Stopped: the pointer or window moved after the screen check. No click sent.";
        Input Mouse(uint flags) => new() { Type = 0, Data = new InputData { Mouse = new MouseInput { Flags = flags } } };
        uint count = SendInput(2, new[] { Mouse(2), Mouse(4) }, InputSize);
        if (count == 1) SendInput(1, new[] { Mouse(4) }, InputSize);
        return $"Windows accepted {count}/2 mouse events. Check whether POL opened the member screen.";
    }

    public static int InputSize => Marshal.SizeOf<Input>();
    public static bool Focus(IntPtr window) { ShowWindowAsync(window, 9); return SetForegroundWindow(window); }
    public static bool IsForeground(int pid, IntPtr window) => GetForegroundWindow() == window && GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)pid;
    public static bool KeysReleased() => new[] { 8, 16, 17, 18, 91, 92 }.All(key => (GetAsyncKeyState(key) & 0x8000) == 0);
    static uint Send(bool releaseOnly)
    {
        Input Key(bool up) => new() { Type = 1, Data = new InputData { Key = new KeyInput { Scan = 0x0e, Flags = 0x0008u | (up ? 0x0002u : 0) } } };
        var inputs = releaseOnly ? new[] { Key(true) } : new[] { Key(false), Key(true) };
        return SendInput((uint)inputs.Length, inputs, InputSize);
    }
    // Separate dispatch boundary allows tests to prove that rejected targets receive zero input.
    public static string Dispatch(Func<bool> valid, Func<bool> foreground, Func<bool> keysReleased, Func<bool, uint>? send = null)
    {
        if (!valid()) return "Stopped: the tracked client changed, closed, or loaded FFXI. No input sent.";
        if (!foreground()) return "Stopped: POL is not the foreground window. No input sent.";
        if (!keysReleased()) return "Stopped: release Backspace and modifier keys, then try again. No input sent.";
        send ??= Send;
        uint count = send(false);
        if (count == 1)
        {
            uint released = send(true);
            return $"Partial input: Windows accepted 1/2 events; extra key-release accepted {released}/1. Check the field before retrying.";
        }
        return count == 2 ? "Windows accepted 2/2 events (one Backspace). Check POL: did one masked character disappear?"
            : "Windows accepted 0/2 input events. No successful input delivery reported.";
    }
}

public sealed class PrivatePolInputForm : Form
{
    readonly Func<IReadOnlyList<int>> tracked;
    readonly Action<string> log;
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(480, 0), Text = "Ready to check the waiting POL client." };
    readonly Button test = new() { Text = "Send one Backspace to POL", AutoSize = true };
    readonly Button clickMember = new() { Text = "Test member click", AutoSize = true };
    readonly Button fillSaved = new() { Text = "Fill saved password", AutoSize = true, Enabled = false };
    readonly Button typeDummy = new() { Text = "Type dummy test", AutoSize = true };
    readonly CheckBox emptyReady = new() { Text = "Empty POL typing panel open; text field focused", AutoSize = true };
    static readonly string[] teachSteps = { "member row", "Log In", "Square Enix Password field", "left edge of the empty typing field" };
    sealed record LoginPath(string WindowTitle, PrivatePolInput.Anchor[] Anchors, PrivatePolInput.Anchor[]? ConnectAnchors = null);
    static string Character(string title) => title.Split(" | ", 2, StringSplitOptions.None)[0];
    static string PathFile(string title)
    {
        var label = Character(title).Trim().ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)));
        return Path.Combine(Path.GetDirectoryName(Config.FilePath)!, $"pol-path-{key}.json");
    }

    static bool SameCharacter(string? recordedTitle, string title) =>
        recordedTitle != null && Supported(recordedTitle) && Supported(title) &&
        string.Equals(Character(recordedTitle).Trim(), Character(title).Trim(), StringComparison.OrdinalIgnoreCase);

    // The title suffix changes with controller assignment and quality mode.
    // Replay still verifies the current window identity, client dimensions and every visual anchor.
    static string? ReadPath(string title)
    {
        var file = PathFile(title);
        if (File.Exists(file)) return File.ReadAllText(file);
        var directory = Path.GetDirectoryName(file)!;
        if (!Directory.Exists(directory)) return null;
        foreach (var candidate in Directory.EnumerateFiles(directory, "private-pol-path*.json"))
        {
            try
            {
                var json = File.ReadAllText(candidate);
                if (SameCharacter(System.Text.Json.JsonSerializer.Deserialize<LoginPath>(json)?.WindowTitle, title))
                    return json;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return null;
    }

    public static bool Supported(string title) =>
        title.Contains(" | ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(Character(title));

    public static string? PathBlockReason(string title)
    {
        if (!Supported(title)) return "Wait for the named PlayOnline window to open.";
        try
        {
            var json = ReadPath(title);
            var path = json == null ? null : System.Text.Json.JsonSerializer.Deserialize<LoginPath>(json);
            if (path?.Anchors?.Length != teachSteps.Length || !SameCharacter(path.WindowTitle, title) ||
                path.Anchors.Any(a => a?.Signature == null))
                return "Teach the PlayOnline path first.";
            if (path.ConnectAnchors is not { Length: 2 } || path.ConnectAnchors.Any(a => a?.Signature == null))
                return "Teach confirmation and Connect first.";
            return null;
        }
        catch { return "The saved PlayOnline path could not be read."; }
    }

    public static string? WaitingTitle(IEnumerable<int> pids)
    {
        string? title = null;
        foreach (var pid in pids.Distinct())
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (!process.ProcessName.Equals("pol", StringComparison.OrdinalIgnoreCase) ||
                    process.MainWindowHandle == IntPtr.Zero || Launcher.IsGameLoaded(process) != false) continue;
                if (title != null) return null;
                title = process.MainWindowTitle;
            }
            catch { }
        }
        return title;
    }

    readonly List<PrivatePolInput.Anchor> taught = new();
    string? teachingTitle;
    readonly Button teach = new() { Text = "Teach next POL step", AutoSize = true };
    PrivatePolInput.Anchor? taughtConfirm;
    string? confirmationPath;
    readonly Button teachConnect = new() { Text = "Teach confirmation and Connect", AutoSize = true };
    readonly Button replayFill = new() { Text = "Replay and fill password", AutoSize = true };
    readonly Button signIn = new() { Text = "Replay and connect", AutoSize = true };
    readonly Button replay = new() { Text = "Replay saved path", AutoSize = true };
    readonly Label lastStep = new() { AutoSize = true, MaximumSize = new Size(460, 0), Text = "Last completed step: none" };
    readonly Button continueManually = new() { Text = "Continue in PlayOnline", AutoSize = true, Enabled = false };
    Action? bringPlayOnlineForward;
    readonly CancellationTokenSource closed = new();
    public PrivatePolInputForm(Func<IReadOnlyList<int>> tracked, Action<string> log, bool startSignIn = false)
    {
        this.tracked = tracked; this.log = log;
        Text = "PlayOnline setup and input tests"; Width = 580; Height = 740; MinimumSize = new Size(560, 680);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        if (startSignIn)
        {
            Text = "PlayOnline sign-in"; MinimumSize = new Size(520, 245); Size = new Size(560, 305);
            layout.Controls.Add(new Label { Text = "Keep PlayOnline visible and leave the mouse still while Offhand signs in.", AutoSize = true, MaximumSize = new Size(460, 0) });
            layout.Controls.Add(lastStep);
            layout.Controls.Add(status);
            continueManually.Click += (_, _) =>
            {
                Close();
                bringPlayOnlineForward?.Invoke();
            };
            layout.Controls.Add(continueManually);
            var cancel = new Button { Text = "Close / stop", AutoSize = true };
            cancel.Click += (_, _) => Close();
            layout.Controls.Add(cancel);
            Controls.Add(layout);
            FormClosing += (_, _) => closed.Cancel();
            Shown += async (_, _) =>
            {
                await RunTest(replaying: true, fillAfterReplay: true, connect: true);
                if (!IsDisposed) continueManually.Enabled = bringPlayOnlineForward != null;
            };
            return;
        }
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = "1. Launch a client through this Offhand session.\n2. Open POL's password typing panel and focus its field.\n3. Click the test below and release all keys.\n\nBackspace test: enter dummy text first. Typing test: leave the field empty. Both focus the single tracked, waiting POL window. Neither reads a password, presses Enter or connects. Return here to read the result.", Margin = new Padding(0, 0, 0, 16) });
        layout.Controls.Add(test);
        layout.Controls.Add(new Label { AutoSize = true, Text = "Dummy typing test: " + PrivatePolInput.DummyText });
        layout.Controls.Add(emptyReady); layout.Controls.Add(typeDummy); layout.Controls.Add(fillSaved);
        typeDummy.Enabled = false;
        emptyReady.CheckedChanged += (_, _) => fillSaved.Enabled = typeDummy.Enabled = emptyReady.Checked && test.Enabled;
        fillSaved.Click += async (_, _) => await RunTest(filling: true);
        typeDummy.Click += async (_, _) => await RunTest(true);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = "Member List test: start the test, then hover over the desired member within five seconds." });
        layout.Controls.Add(clickMember);
        clickMember.Click += async (_, _) => await RunTest(click: true);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0),
            Text = "Start at Member List. Teach each control in order by hovering without clicking. The last step records the empty field and stops." });
        layout.Controls.Add(teach);
        layout.Controls.Add(replay);
        layout.Controls.Add(replayFill);
        layout.Controls.Add(signIn);
        signIn.Click += async (_, _) => await RunTest(replaying: true, fillAfterReplay: true, connect: true);
        layout.Controls.Add(teachConnect);
        teachConnect.Click += async (_, _) => await RunTest(teachingConnect: true);
        replayFill.Click += async (_, _) => await RunTest(replaying: true, fillAfterReplay: true);
        teach.Click += async (_, _) => await RunTest(teaching: true);
        replay.Click += async (_, _) => await RunTest(replaying: true);
        status.Margin = new Padding(0, 14, 0, 10); layout.Controls.Add(status);
        var close = new Button { Text = "Close", AutoSize = true }; close.Click += (_, _) => Close(); layout.Controls.Add(close);
        Controls.Add(layout); test.Click += async (_, _) => await RunTest();
        FormClosing += (_, _) => closed.Cancel();
    }
    async Task RunTest(bool typing = false, bool click = false, bool teaching = false, bool replaying = false, bool filling = false, bool fillAfterReplay = false, bool teachingConnect = false, bool connect = false)
    {
        if (!test.Enabled) return;
        if ((typing || filling) && !emptyReady.Checked) return;
        test.Enabled = false; fillSaved.Enabled = false; typeDummy.Enabled = false; clickMember.Enabled = false; teach.Enabled = false; replay.Enabled = false; replayFill.Enabled = false; teachConnect.Enabled = false; signIn.Enabled = false;
        try
        {
            var candidates = new List<(int Pid, IntPtr Window, DateTime Started, string Title)>();
            foreach (var pid in tracked().Distinct())
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (process.ProcessName.Equals("pol", StringComparison.OrdinalIgnoreCase) && process.MainWindowHandle != IntPtr.Zero && Launcher.IsGameLoaded(process) == false)
                        candidates.Add((pid, process.MainWindowHandle, process.StartTime, process.MainWindowTitle));
                }
                catch { }
            }
            if (candidates.Count != 1) { Report("Stopped: expected exactly one tracked POL client waiting to load FFXI. Launch it through Offhand first. No input sent."); return; }
            var target = candidates[0];
            bool Valid()
            {
                try
                {
                    if (!tracked().Contains(target.Pid)) return false;
                    using var p = System.Diagnostics.Process.GetProcessById(target.Pid);
                    return p.ProcessName.Equals("pol", StringComparison.OrdinalIgnoreCase) && p.StartTime == target.Started && p.MainWindowHandle == target.Window && p.MainWindowTitle == target.Title && Launcher.IsGameLoaded(p) == false;
                }
                catch { return false; }
            }
            bringPlayOnlineForward = () =>
            {
                if (Valid()) PrivatePolInput.Focus(target.Window);
            };
            if (teaching || replaying || filling || teachingConnect)
            {
                if (!Supported(target.Title))
                {
                    Report("Stopped: wait for Offhand to name the PlayOnline window.");
                    return;
                }
                if (teachingConnect) await TeachConnectStep(target.Window, target.Title, Valid);
                else if (teaching) await TeachStep(target.Window, target.Title, Valid);
                else if (replaying) await ReplayPath(target.Pid, target.Window, target.Title, Valid, fillAfterReplay, connect);
                else await FillPassword(target.Pid, target.Window, target.Title, Valid);
                return;
            }
            var plan = typing ? PrivatePolInput.DummyPlan(target.Window) : Array.Empty<PrivatePolInput.Stroke>();
            if (!Valid() || !PrivatePolInput.KeysReleased()) { Report("Stopped: client unavailable or a modifier/Backspace key is held. No input sent."); return; }
            if (!PrivatePolInput.Focus(target.Window)) { Report("Stopped: Windows did not grant foreground focus. No input sent."); return; }
            if (click)
            {
                Report("Hover over the desired POL member now. One click in five seconds.");
                await Task.Delay(5000, closed.Token);
                closed.Token.ThrowIfCancellationRequested();
                if (!Valid()) { Report("Stopped: the tracked POL client changed or loaded FFXI. No click sent."); return; }
                // Offhand or another window can take focus while the user positions the pointer.
                // Bring POL forward again immediately before validating the click target.
                PrivatePolInput.Focus(target.Window);
                await Task.Delay(100, closed.Token);
                closed.Token.ThrowIfCancellationRequested();
                Report(PrivatePolInput.ClickAtPointer(target.Window, Valid));
                return;
            }
            await Task.Delay(350, closed.Token);
            if (typing) Report(await PrivatePolInput.TypePlan(plan, Valid, () => PrivatePolInput.IsForeground(target.Pid, target.Window), PrivatePolInput.KeysReleased, closed.Token));
            else Report(PrivatePolInput.Dispatch(Valid, () => PrivatePolInput.IsForeground(target.Pid, target.Window), PrivatePolInput.KeysReleased));
        }
        catch (OperationCanceledException) { }
        catch { Report("Diagnostic failed. No password was read or login submitted. Check POL before retrying."); }
        finally { if (!IsDisposed) { test.Enabled = true; clickMember.Enabled = true; teach.Enabled = true; replay.Enabled = true; replayFill.Enabled = true; teachConnect.Enabled = true; signIn.Enabled = true; emptyReady.Checked = false; typeDummy.Enabled = false; fillSaved.Enabled = false; } }
    }
    async Task TeachStep(IntPtr window, string title, Func<bool> valid)
    {
        if (teachingTitle != title) { taught.Clear(); teachingTitle = title; }
        int index = taught.Count;
        Report($"Step {index + 1}/{teachSteps.Length}: hover over {(index == 0 ? Character(title) + " member row" : teachSteps[index])} now. Do not click.");
        PrivatePolInput.Focus(window);
        await Task.Delay(5000, closed.Token);
        closed.Token.ThrowIfCancellationRequested();
        if (!valid() || PrivatePolInput.ReadAnchor(window) is not { } anchor)
        {
            Report("Stopped: keep the pointer over the visible PlayOnline control. This step was not recorded.");
            return;
        }
        if (!PrivatePolInput.HasAnchorDetail(anchor))
        {
            Report("Stopped: this area has too little detail. Hover over the control label or the left edge of the empty field.");
            return;
        }
        if (index == teachSteps.Length - 1)
        {
            var path = new LoginPath(title, taught.Append(anchor).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile(title))!);
            File.WriteAllText(PathFile(title) + ".tmp", System.Text.Json.JsonSerializer.Serialize(path));
            File.Move(PathFile(title) + ".tmp", PathFile(title), overwrite: true);
            taught.Clear();
            Report($"Saved the {Character(title)} path. Replay starts at Member List and stops at the typing panel.");
            return;
        }
        var result = PrivatePolInput.ClickAtPointer(window, valid, anchor);
        if (!result.StartsWith("Windows accepted 2/2", StringComparison.Ordinal))
        {
            Report(result);
            return;
        }
        taught.Add(anchor);
        Report($"Step {index + 1} sent. Verify PlayOnline advanced, then teach {teachSteps[index + 1]}. Close and reopen this dialog to start teaching over.");
    }

    async Task TeachConnectStep(IntPtr window, string title, Func<bool> valid)
    {
        var saved = ReadPath(title);
        var path = saved == null ? null : System.Text.Json.JsonSerializer.Deserialize<LoginPath>(saved);
        if (path?.Anchors?.Length != teachSteps.Length || !SameCharacter(path.WindowTitle, title))
        {
            Report("Stopped: save this character's pre-password path first.");
            return;
        }
        if (confirmationPath != saved) taughtConfirm = null;
        bool first = taughtConfirm == null;
        Report(first ? "Step 1/2: on the typing panel, hover over the round confirmation button for five seconds. Do not click yet."
                     : "Step 2/2: on the Connect screen, hover over Connect for five seconds. Do not click yet.");
        PrivatePolInput.Focus(window);
        await Task.Delay(5000, closed.Token);
        closed.Token.ThrowIfCancellationRequested();
        if (!valid() || PrivatePolInput.ReadAnchor(window) is not { } anchor || !PrivatePolInput.HasAnchorDetail(anchor))
        {
            Report("Stopped: hover over the visible button so Offhand can record its details.");
            return;
        }
        if (first)
        {
            taughtConfirm = anchor;
            confirmationPath = saved;
            Report("Confirmation recorded. Click the round button yourself, then return here and choose Teach confirmation and Connect again.");
            return;
        }
        if (taughtConfirm == anchor)
        {
            Report("Stopped: this looks like the same control. Open the Connect screen first.");
            return;
        }
        var extended = path with { ConnectAnchors = new[] { taughtConfirm!, anchor } };
        File.WriteAllText(PathFile(title) + ".tmp", System.Text.Json.JsonSerializer.Serialize(extended));
        File.Move(PathFile(title) + ".tmp", PathFile(title), overwrite: true);
        taughtConfirm = null;
        confirmationPath = null;
        Report("Saved both controls. You can click Connect manually.");
    }

    async Task FillPassword(int pid, IntPtr window, string title, Func<bool> valid)
    {
        var json = ReadPath(title);
        var path = json == null ? null : System.Text.Json.JsonSerializer.Deserialize<LoginPath>(json);
        if (path?.Anchors?.Length != teachSteps.Length || !SameCharacter(path.WindowTitle, title) ||
            path.Anchors[^1] is not { Signature: not null } anchor)
        {
            Report("Stopped: teach this character's empty typing panel first.");
            return;
        }
        PrivatePolInput.Focus(window);
        await Task.Delay(350, closed.Token);
        bool Foreground() => PrivatePolInput.IsForeground(pid, window);
        if (!valid() || !Foreground() || !PrivatePolInput.MoveToAnchor(window, anchor))
        {
            Report("Stopped: the typing panel is not in front or its size changed.");
            return;
        }
        var failure = await WaitForAnchor(window, anchor, valid, Foreground);
        if (failure != null)
        {
            Report($"Stopped before password entry: {failure}");
            return;
        }
        await SendSavedPassword(Character(title), window, valid, Foreground);
    }

    async Task<bool> SendSavedPassword(string character, IntPtr window, Func<bool> valid, Func<bool> foreground)
    {
        if (!valid() || !foreground())
        {
            Report("Stopped: PlayOnline lost keyboard focus before password entry.");
            return false;
        }
        var plan = new WindowsPrivateLoginStore().PasswordPlan(character, window);
        if (plan == null)
        {
            Report($"Stopped: save the password under the {character} member label in PlayOnline sign-in settings.");
            return false;
        }
        try
        {
            Report("Entering saved password. If interrupted, clear the field before retrying.");
            var result = await PrivatePolInput.TypePlan(plan, valid, foreground, PrivatePolInput.KeysReleased,
                closed.Token, savedPassword: true);
            if (result == PrivatePolInput.PasswordSent) Completed("saved password input sent");
            Report(result == PrivatePolInput.PasswordSent ? result : result + " Clear any partial input before retrying.");
            return result == PrivatePolInput.PasswordSent;
        }
        finally { Array.Clear(plan); }
    }

    async Task ReplayPath(int pid, IntPtr window, string title, Func<bool> valid, bool fillAfterReplay, bool connect)
    {
        var json = ReadPath(title);
        var path = json == null ? null : System.Text.Json.JsonSerializer.Deserialize<LoginPath>(json);
        if (path?.Anchors?.Length != teachSteps.Length || !SameCharacter(path.WindowTitle, title) || path.Anchors.Any(anchor => anchor?.Signature == null))
        {
            Report("Stopped: teach a new path for this character window first. Older exact-match paths need to be retaught.");
            return;
        }
        if (connect && (path.ConnectAnchors is not { Length: 2 } || path.ConnectAnchors.Any(a => a?.Signature == null)))
        {
            Report("Stopped: teach confirmation and Connect for this character first.");
            return;
        }
        PrivatePolInput.Focus(window);
        for (int i = 0; i < path.Anchors.Length; i++)
        {
            var anchor = path.Anchors[i];
            Report($"Checking {teachSteps[i]}…");
            await Task.Delay(i == 0 ? 250 : 1000, closed.Token);
            if (!valid() || !PrivatePolInput.MoveToAnchor(window, anchor))
            {
                Report($"Stopped before {teachSteps[i]}: target or window size changed.");
                return;
            }
            var failure = await WaitForAnchor(window, anchor, valid);
            if (failure != null)
            {
                Report($"Stopped before {teachSteps[i]}: {failure}");
                return;
            }
            if (i == path.Anchors.Length - 1)
            {
                Completed("password typing panel matched");
                if (fillAfterReplay)
                {
                    bool Foreground() => PrivatePolInput.IsForeground(pid, window);
                    if (await SendSavedPassword(Character(title), window, valid, Foreground) && connect)
                        await ReplayConnect(window, path.ConnectAnchors!, valid);
                    return;
                }
                break;
            }
            var result = PrivatePolInput.ClickAtPointer(window, valid, anchor);
            if (!result.StartsWith("Windows accepted 2/2", StringComparison.Ordinal))
            {
                Report(result);
                return;
            }
            Completed($"{teachSteps[i]} click sent");
        }
        Report("Replay matched the typing panel and stopped.");
    }

    async Task ReplayConnect(IntPtr window, PrivatePolInput.Anchor[] anchors, Func<bool> valid)
    {
        for (int i = 0; i < anchors.Length; i++)
        {
            var anchor = anchors[i];
            var step = i == 0 ? "typing confirmation" : "Connect";
            Report($"Checking {step}…");
            if (!valid() || !PrivatePolInput.MoveToAnchor(window, anchor))
            {
                Report($"Stopped before {step}: the PlayOnline target changed.");
                return;
            }
            var failure = await WaitForAnchor(window, anchor, valid);
            if (failure != null)
            {
                Report($"Stopped before {step}: {failure}");
                return;
            }
            var result = PrivatePolInput.ClickAtPointer(window, valid, anchor);
            if (!result.StartsWith("Windows accepted 2/2", StringComparison.Ordinal))
            {
                Report(result);
                return;
            }
            Completed($"{step} click sent");
        }
        Report("Connect click sent once. PlayOnline is handling sign-in. Check the game window for the result.");
    }

    async Task<string?> WaitForAnchor(IntPtr window, PrivatePolInput.Anchor anchor,
        Func<bool> valid, Func<bool>? foreground = null)
    {
        bool visible = false;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(250, closed.Token);
            closed.Token.ThrowIfCancellationRequested();
            if (!valid()) return "the tracked PlayOnline window changed.";
            if (foreground != null && !foreground()) return "PlayOnline lost keyboard focus.";
            if (PrivatePolInput.ReadAnchor(window) is not { } observed) continue;
            if (observed.X != anchor.X || observed.Y != anchor.Y) return "the pointer moved.";
            visible = true;
            if (PrivatePolInput.MatchesAnchor(anchor, observed)) return null;
        }
        return visible ? "the visible control did not match the taught step."
                       : "the taught area is covered or outside the visible PlayOnline window.";
    }

    void Completed(string step) => lastStep.Text = "Last completed step: " + step;

    void Report(string text) { status.Text = text; log("PRIVATE INPUT: " + text); }
}
