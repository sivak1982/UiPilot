namespace WinFormsSampleApp;

public sealed class MainForm : Form
{
    private readonly TextBox _nameBox;
    private readonly Label _greetingText;
    private readonly ComboBox _colorCombo;
    private readonly ListBox _historyList;

    public MainForm()
    {
        Name = "MainForm";
        Text = "UiPilot WinForms Sample";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);
        ClientSize = new Size(760, 520);

        var menu = BuildMenu();
        var tools = BuildToolStrip();

        var title = new Label
        {
            Name = "TitleText",
            Text = "WinForms automation sample",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12),
        };

        var nameLabel = new Label
        {
            Name = "NameLabel",
            Text = "Name",
            AutoSize = true,
        };

        _nameBox = new TextBox
        {
            Name = "NameBox",
            AccessibleName = "Name",
            Width = 320,
        };

        var greetButton = new Button
        {
            Name = "GreetButton",
            Text = "Greet",
            AutoSize = true,
        };
        greetButton.Click += (_, _) => Greet();

        _greetingText = new Label
        {
            Name = "GreetingText",
            Text = "Ready",
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 12),
        };

        var colorLabel = new Label
        {
            Name = "ColorLabel",
            Text = "Favorite color",
            AutoSize = true,
        };

        _colorCombo = new ComboBox
        {
            Name = "ColorCombo",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
        };
        _colorCombo.Items.AddRange(["Blue", "Green", "Orange"]);
        _colorCombo.SelectedIndex = 0;

        _historyList = new ListBox
        {
            Name = "HistoryList",
            Width = 420,
            Height = 140,
        };

        var content = new FlowLayoutPanel
        {
            Name = "ContentPanel",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(24),
        };
        content.Controls.AddRange(
        [
            title,
            nameLabel,
            _nameBox,
            greetButton,
            _greetingText,
            colorLabel,
            _colorCombo,
            _historyList,
        ]);

        Controls.Add(content);
        Controls.Add(tools);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Name = "MainMenu" };
        var actions = new ToolStripMenuItem("&Actions") { Name = "ActionsMenu" };
        var markReady = new ToolStripMenuItem("&Mark ready") { Name = "MarkReadyMenuItem" };
        markReady.Click += (_, _) => _greetingText.Text = "Menu action completed";
        actions.DropDownItems.Add(markReady);
        menu.Items.Add(actions);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var tools = new ToolStrip { Name = "MainToolStrip" };
        var clear = new ToolStripButton("Clear")
        {
            Name = "ClearToolButton",
            AccessibleName = "Clear history",
        };
        clear.Click += (_, _) => _historyList.Items.Clear();
        tools.Items.Add(clear);
        return tools;
    }

    private void Greet()
    {
        var name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "World" : _nameBox.Text.Trim();
        var greeting = $"Hello, {name}!";
        _greetingText.Text = greeting;
        _historyList.Items.Add($"{greeting} Color: {_colorCombo.SelectedItem}");
    }
}
