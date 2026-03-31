using System.ComponentModel;
using LargeDataExportWithUI.App.Models;
using LargeDataExportWithUI.App.Services;

namespace LargeDataExportWithUI.App;

public sealed class MainForm : Form
{
    private const int KeywordPreviewLimit = 10_000;
    private const int StatusQueryPreviewLimit = 240;
    private const int WorkspacePanel1PreferredWidth = 760;
    private const int WorkspacePanel1MinWidth = 500;
    private const int WorkspacePanel2MinWidth = 280;

    private readonly AppSettingsStore _settingsStore = new();
    private readonly SessionStateStore _sessionStateStore = new();
    private readonly ExecutionValidationService _executionValidationService = new();
    private readonly ExtractionStrategyFactory _strategyFactory = new();
    private readonly SalesforceAuthenticationService _salesforceAuthenticationService = new();
    private readonly SalesforceConnectedAppLoginService _connectedAppLoginService = new();
    private readonly System.Windows.Forms.Timer _saveDebounceTimer;

    private AppSettings _settings = new();
    private CancellationTokenSource? _executionCancellation;
    private CancellationTokenSource? _loginCancellation;
    private SessionState? _sessionState;
    private bool _hasSessionAuthentication;
    private bool _isLoaded;
    private bool _suppressSaveSchedule;
    private bool _workspaceSplitInitialized;
    private string _lastKeywordLogSignature = string.Empty;

    private readonly TextBox _soqlEditor;
    private readonly TextBox _keywordsEditor;
    private readonly PropertyGrid _propertyGrid;
    private readonly RichTextBox _statusView;
    private readonly Button _flushButton;
    private readonly Button _loginButton;
    private readonly Button _runButton;
    private readonly TextBox _keywordModeLabel;
    private readonly TextBox _loginModeLabel;
    private readonly TextBox _outputPathLabel;
    private readonly TextBox _callbackUrlLabel;
    private readonly SplitContainer _workspaceSplit;

    public MainForm()
    {
        Text = "Large Data Export With UI";
        MinimumSize = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;

        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("LargeDataExportWithUI.App.18477408.ico");
        if (iconStream is not null)
        {
            Icon = new Icon(iconStream);
        }

        _saveDebounceTimer = new System.Windows.Forms.Timer
        {
            Interval = 700,
        };
        _saveDebounceTimer.Tick += SaveDebounceTimer_Tick;

        _soqlEditor = CreateEditorTextBox();
        _keywordsEditor = CreateEditorTextBox();
        _propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = true,
            ToolbarVisible = false,
            PropertySort = PropertySort.Categorized,
        };
        _propertyGrid.PropertyValueChanged += PropertyGrid_PropertyValueChanged;

        _statusView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            Font = new Font("Consolas", 10.0f, FontStyle.Regular, GraphicsUnit.Point),
            HideSelection = false,
        };

        _flushButton = new Button
        {
            Text = "Flush",
            AutoSize = true,
        };
        _flushButton.Click += FlushButton_Click;

        _loginButton = new Button
        {
            Text = "Login",
            AutoSize = true,
        };
        _loginButton.Click += LoginButton_Click;

        _runButton = new Button
        {
            Text = "Run",
            AutoSize = true,
        };
        _runButton.Click += RunButton_Click;

        _keywordModeLabel = CreateHintTextBox();
        _loginModeLabel = CreateHintTextBox();
        _outputPathLabel = CreateHintTextBox();
        _callbackUrlLabel = CreateHintTextBox();
        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };

        _soqlEditor.TextChanged += EditorStateChanged;
        _keywordsEditor.TextChanged += EditorStateChanged;

        BuildLayout();

        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        InitializeWorkspaceSplitIfNeeded();
        await LoadStateAsync();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _saveDebounceTimer.Stop();

        if (!_isLoaded)
        {
            return;
        }

        CaptureEditorsIntoSettings();
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Ignore final save failures during shutdown.
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8),
        };
        actionsPanel.Controls.AddRange([_flushButton, _loginButton, _runButton]);

        var hintsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8, 0, 0, 8),
        };
        hintsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hintsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintsPanel.Controls.Add(_keywordModeLabel, 0, 0);
        hintsPanel.Controls.Add(_loginModeLabel, 0, 1);
        hintsPanel.Controls.Add(_outputPathLabel, 0, 2);
        hintsPanel.Controls.Add(_callbackUrlLabel, 0, 3);

        var editorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        editorLayout.Controls.Add(CreateSectionLabel("SOQL Template"), 0, 0);
        editorLayout.Controls.Add(_soqlEditor, 0, 1);
        editorLayout.Controls.Add(CreateSectionLabel("Keywords"), 0, 2);
        editorLayout.Controls.Add(_keywordsEditor, 0, 3);

        var propertyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        propertyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        propertyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        propertyLayout.Controls.Add(CreateSectionLabel("Configurations"), 0, 0);
        propertyLayout.Controls.Add(_propertyGrid, 0, 1);

        _workspaceSplit.Panel1.Controls.Add(editorLayout);
        _workspaceSplit.Panel2.Controls.Add(propertyLayout);

        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusLayout.Controls.Add(CreateSectionLabel("Status View"), 0, 0);
        statusLayout.Controls.Add(_statusView, 0, 1);

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.Controls.Add(actionsPanel, 0, 0);
        headerLayout.Controls.Add(hintsPanel, 1, 0);

        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(_workspaceSplit, 0, 1);
        rootLayout.Controls.Add(statusLayout, 0, 2);

        Controls.Add(rootLayout);
        ResumeLayout(true);
    }

    private void InitializeWorkspaceSplitIfNeeded()
    {
        if (_workspaceSplitInitialized)
        {
            return;
        }

        var availableWidth = _workspaceSplit.ClientSize.Width;
        if (availableWidth <= 0)
        {
            return;
        }

        _workspaceSplit.Panel1MinSize = WorkspacePanel1MinWidth;
        _workspaceSplit.Panel2MinSize = WorkspacePanel2MinWidth;

        var minimumPanel1Width = WorkspacePanel1MinWidth;
        var maximumPanel1Width = availableWidth - WorkspacePanel2MinWidth - _workspaceSplit.SplitterWidth;
        if (maximumPanel1Width < minimumPanel1Width)
        {
            return;
        }

        var targetPanel1Width = Math.Clamp(WorkspacePanel1PreferredWidth, minimumPanel1Width, maximumPanel1Width);
        _workspaceSplit.SplitterDistance = targetPanel1Width;
        _workspaceSplitInitialized = true;
    }

    private async Task LoadStateAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            _sessionState = await _sessionStateStore.LoadAsync();
            _settings.EnsureDefaults();
            _hasSessionAuthentication = _sessionState?.IsValidAt(DateTimeOffset.UtcNow) == true;

            _suppressSaveSchedule = true;
            _propertyGrid.SelectedObject = _settings;
            _soqlEditor.Text = _settings.SoqlTemplate;
            _keywordsEditor.Text = _settings.KeywordsText;
            _suppressSaveSchedule = false;

            AppendStatus(StatusSeverity.Info, "Application startup completed.");
            AppendStatus(StatusSeverity.Info, "Configuration loaded.");
            AppendStatus(StatusSeverity.Info, _hasSessionAuthentication
                ? $"Stored session state is available for: {_sessionState!.InstanceUrl}"
                : "No valid stored session state is available.");
            await TryLoadKeywordsFromConfiguredFileAsync(logIfMissing: false);
            if (!_settings.IsKeywordFileMode && !string.IsNullOrWhiteSpace(_settings.KeywordsText))
            {
                LogKeywordAnalysis("Direct paste", KeywordBatchPlanner.AnalyzeKeywords(GetKeywordsTextForProcessing(), _settings.Delimiter));
            }
            ApplyInteractionState();
        }
        catch (Exception exception)
        {
            AppendStatus(StatusSeverity.Error, $"Failed to load configuration: {exception.Message}");
            _settings = new AppSettings();
            _propertyGrid.SelectedObject = _settings;
            ApplyInteractionState();
        }
    }

    private static TextBox CreateEditorTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = 0,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false,
            Font = new Font("Consolas", 10.5f, FontStyle.Regular, GraphicsUnit.Point),
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6),
            Font = new Font("Segoe UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point),
        };
    }

    private static TextBox CreateHintTextBox()
    {
        return new TextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Control.DefaultBackColor,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
            Dock = DockStyle.Fill,
            TabStop = false,
        };
    }

    private void EditorStateChanged(object? sender, EventArgs e)
    {
        if (!_isLoaded || _suppressSaveSchedule)
        {
            return;
        }

        CaptureEditorsIntoSettings();
        _propertyGrid.Refresh();
        LogKeywordAnalysis("Direct paste", KeywordBatchPlanner.AnalyzeKeywords(GetKeywordsTextForProcessing(), _settings.Delimiter));
        ApplyInteractionState();
        ScheduleSave();
    }

    private async void PropertyGrid_PropertyValueChanged(object? sender, PropertyValueChangedEventArgs e)
    {
        var changedLabel = e.ChangedItem?.Label ?? string.Empty;

        if (changedLabel == nameof(AppSettings.OrgType) || changedLabel == "Org Type")
        {
            var previousOrgType = e.OldValue is OrgType oldValue ? oldValue : _settings.OrgType;
            var previousDefaultUrl = AppSettings.GetDefaultLoginUrl(previousOrgType);
            var currentDefaultUrl = AppSettings.GetDefaultLoginUrl(_settings.OrgType);

            var loginUrlChanged = false;
            if (string.IsNullOrWhiteSpace(_settings.LoginUrl) || string.Equals(_settings.LoginUrl, previousDefaultUrl, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LoginUrl = currentDefaultUrl;
                loginUrlChanged = true;
                _propertyGrid.Refresh();
            }

            AppendStatus(StatusSeverity.Configuration, $"Org Type changed to {_settings.OrgType}. Login URL is {_settings.LoginUrl}.");

            if (loginUrlChanged)
            {
                await InvalidateSessionStateAsync("Login URL changed due to Org Type change. Stored session has been cleared. Please login again.");
            }
        }

        if (changedLabel == nameof(AppSettings.KeywordsSource) || changedLabel == "Keywords Source")
        {
            await TryLoadKeywordsFromConfiguredFileAsync(logIfMissing: true);
        }

        if (changedLabel == nameof(AppSettings.Delimiter) || changedLabel == "Delimiter")
        {
            await ReevaluateKeywordStateAsync();
        }

        if (changedLabel == nameof(AppSettings.SkipFirstRow) || changedLabel == "Skip First Row")
        {
            await ReevaluateKeywordStateAsync();
        }

        if (changedLabel == nameof(AppSettings.KeywordValueType) || changedLabel == "Keyword Value Type")
        {
            AppendStatus(StatusSeverity.Configuration, $"Keyword Value Type changed to {_settings.KeywordValueType}.");
        }

        if (changedLabel == nameof(AppSettings.LoginMethod) || changedLabel == "Login Method")
        {
            _hasSessionAuthentication = _sessionState?.IsValidAt(DateTimeOffset.UtcNow) == true;
            AppendStatus(StatusSeverity.Configuration, $"Login Method changed to {_settings.LoginMethod}.");
            if (_settings.LoginMethod == LoginMethod.Session && _hasSessionAuthentication && _sessionState is not null)
            {
                AppendStatus(StatusSeverity.Warning, $"Stored session is for: {_sessionState.InstanceUrl}. If this is not the intended org, click Login to authenticate again.");
            }
        }

        if (changedLabel == nameof(AppSettings.ClientId) || changedLabel == "Client ID")
        {
            AppendStatus(StatusSeverity.Configuration, "Connected App Client ID was updated.");
        }

        if (changedLabel == nameof(AppSettings.CallbackPort) || changedLabel == "Callback Port")
        {
            AppendStatus(StatusSeverity.Configuration, $"Callback Port changed to {_settings.CallbackPort}.");
        }

        if (changedLabel == nameof(AppSettings.LoginUrl) || changedLabel == "Login URL")
        {
            AppendStatus(StatusSeverity.Configuration, $"Login URL changed to {_settings.LoginUrl}.");
            await InvalidateSessionStateAsync("Login URL changed. Stored session has been cleared. Please login again.");
        }

        if (changedLabel == nameof(AppSettings.ExtractionMethod) || changedLabel == "Extraction Method")
        {
            AppendStatus(StatusSeverity.Configuration, $"Extraction Method changed to {ExtractionMethodConverter.ToDisplayName(_settings.ExtractionMethod)}.");
        }

        AppendStatus(StatusSeverity.Configuration, $"Configuration changed: {changedLabel}.");
        _propertyGrid.Refresh();
        ApplyInteractionState();
        ScheduleSave();
    }

    private void FlushButton_Click(object? sender, EventArgs e)
    {
        _suppressSaveSchedule = true;
        _settings.KeywordsSource = string.Empty;
        _settings.KeywordsText = string.Empty;
        _keywordsEditor.Clear();
        _suppressSaveSchedule = false;
        _lastKeywordLogSignature = string.Empty;
        CaptureEditorsIntoSettings();
        _propertyGrid.Refresh();
        ApplyInteractionState();
        ScheduleSave();
        AppendStatus(StatusSeverity.Info, "Keyword state was cleared.");
    }

    private async void LoginButton_Click(object? sender, EventArgs e)
    {
        if (_loginCancellation is not null)
        {
            _loginCancellation.Cancel();
            return;
        }

        CaptureEditorsIntoSettings();

        try
        {
            _loginCancellation = new CancellationTokenSource();
            ApplyInteractionState(isRunning: true, isLoginInProgress: true);
            AppendStatus(StatusSeverity.Info, $"Login started. Opening PKCE authentication at: {_settings.LoginUrl}");

            if (_settings.LoginMethod == LoginMethod.Password)
            {
                throw new InvalidOperationException("Login is disabled in Password mode.");
            }

            _sessionState = await _connectedAppLoginService.LoginAsync(_settings, _loginCancellation.Token);
            await _sessionStateStore.SaveAsync(_sessionState);
            _hasSessionAuthentication = true;
            AppendStatus(StatusSeverity.Info, $"Login succeeded. Session stored for: {_sessionState.InstanceUrl}.");
        }
        catch (OperationCanceledException)
        {
            AppendStatus(StatusSeverity.Warning, "Login canceled by user.");
        }
        catch (Exception exception)
        {
            _hasSessionAuthentication = _sessionState?.IsValidAt(DateTimeOffset.UtcNow) == true;
            AppendStatus(StatusSeverity.Error, $"Login failed: {exception.Message}");
        }
        finally
        {
            _loginCancellation?.Dispose();
            _loginCancellation = null;
            ApplyInteractionState(isRunning: false);
        }
    }

    private async void RunButton_Click(object? sender, EventArgs e)
    {
        if (_executionCancellation is not null)
        {
            _executionCancellation.Cancel();
            return;
        }

        CaptureEditorsIntoSettings();

        try
        {
            if (!await PromptForOutputFilePathAsync())
            {
                AppendStatus(StatusSeverity.Warning, "Run canceled because no output file was selected.");
                ApplyInteractionState();
                return;
            }

            await TryLoadKeywordsFromConfiguredFileAsync(logIfMissing: true, loadOnlyWhenEditorEmpty: true);

            var validationResult = _executionValidationService.Validate(_settings, _sessionState);
            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    AppendStatus(StatusSeverity.Validation, error);
                }

                throw new InvalidOperationException("Execution validation failed.");
            }

            var preparation = KeywordBatchPlanner.BuildBatches(
                _settings.SoqlTemplate,
                _settings.InClauseToken,
                GetKeywordsTextForProcessing(),
                _settings.Delimiter,
                _settings.ChunkSize,
                _settings.KeywordValueType);

            AppendStatus(StatusSeverity.Info, "Run started.");
            AppendStatus(
                StatusSeverity.Validation,
                $"Prepared {preparation.Batches.Count} batches from {preparation.InputCount} values. Unique={preparation.UniqueCount}, Duplicates={preparation.DuplicateCount}, Ignored={preparation.IgnoredCount}.");

            AppendStatus(StatusSeverity.Info, $"Execution strategy: {ExtractionMethodConverter.ToDisplayName(_settings.ExtractionMethod)}.");
            if (_settings.ExtractionMethod == ExtractionMethod.RestQueryAll)
            {
                AppendStatus(StatusSeverity.Warning, "REST QueryAll is intended only when deleted or archived records must be included.");
            }

            _executionCancellation = new CancellationTokenSource();
            ApplyInteractionState(isRunning: true);

            SalesforceConnectionContext connectionContext;
            try
            {
                AppendStatus(StatusSeverity.Progress, $"Establishing Salesforce connection using {_settings.LoginMethod} mode.");
                connectionContext = await _salesforceAuthenticationService.CreateConnectionAsync(_settings, validationResult, _executionCancellation.Token);
                AppendStatus(StatusSeverity.Info, $"Salesforce connection established against {connectionContext.InstanceUrl}.");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Salesforce connection could not be established: {exception.Message}", exception);
            }

            var summary = await ExecuteBatchesAsync(preparation, validationResult, connectionContext, _executionCancellation.Token);
            AppendStatus(StatusSeverity.Info, $"Execution completed. Successful batches={summary.SuccessfulBatches}, Failed batches={summary.FailedBatches}, Aggregated records={summary.AggregatedRecordCount}.");
        }
        catch (OperationCanceledException)
        {
            AppendStatus(StatusSeverity.Warning, "Execution stopped by user cancellation.");
        }
        catch (Exception exception)
        {
            AppendStatus(StatusSeverity.Error, $"Execution stopped: {exception.Message}");
        }
        finally
        {
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            ApplyInteractionState(isRunning: false);
        }
    }

    private async Task<ExecutionRunSummary> ExecuteBatchesAsync(
        BatchPreparationResult preparation,
        ExecutionValidationResult validationResult,
        SalesforceConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        var successfulBatches = 0;
        var failedBatches = 0;
        var aggregatedRecordCount = 0;
        var strategy = _strategyFactory.GetStrategy(_settings.ExtractionMethod);
        var exportWriter = new CsvExportFileWriter(_settings.OutputFilePath);
        await exportWriter.InitializeAsync(cancellationToken);

        for (var index = 0; index < preparation.Batches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = preparation.Batches[index];
            AppendStatus(
                StatusSeverity.Progress,
                $"Batch started {batch.BatchNumber}/{preparation.Batches.Count}: {batch.Values.Count} items, query length {batch.QueryLength}.");
            AppendStatus(
                StatusSeverity.Progress,
                $"Batch query preview {batch.BatchNumber}/{preparation.Batches.Count}: {BuildQueryPreview(batch.QueryText)}");

            if (batch.WasShrunkForQueryLimit)
            {
                AppendStatus(
                    StatusSeverity.Validation,
                    $"Batch {batch.BatchNumber} was reduced from {batch.RequestedItemCount} to {batch.Values.Count} items to stay within the practical SOQL length limit ({KeywordBatchPlanner.PracticalQueryLengthLimit}).");
            }

            try
            {
                var result = await strategy.ExecuteAsync(batch, connectionContext, validationResult, cancellationToken);
                await exportWriter.AppendBatchAsync(result.CsvContent, cancellationToken);
                successfulBatches++;
                aggregatedRecordCount += result.RecordCount;
                AppendStatus(
                    StatusSeverity.Progress,
                    $"Batch output written {batch.BatchNumber}/{preparation.Batches.Count}: {_settings.OutputFilePath} ({result.RecordCount} records).");
                AppendStatus(
                    StatusSeverity.Progress,
                    $"Batch succeeded {batch.BatchNumber}/{preparation.Batches.Count}: strategy={result.StrategyDisplayName}, records={result.RecordCount}. {result.OutcomeDetail}");
                await Task.Delay(120, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedBatches++;
                AppendStatus(StatusSeverity.Error, $"Batch failed {batch.BatchNumber}/{preparation.Batches.Count}: {exception.Message}");

                if (_settings.LoginMethod == LoginMethod.Password && batch.BatchNumber == 1)
                {
                    throw new InvalidOperationException("Password mode stopped after the first chunk failure.", exception);
                }

                throw;
            }
        }

        return new ExecutionRunSummary(successfulBatches, failedBatches, aggregatedRecordCount);
    }

    private async Task TryLoadKeywordsFromConfiguredFileAsync(bool logIfMissing, bool loadOnlyWhenEditorEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(_settings.KeywordsSource))
        {
            ApplyKeywordEditorContent(_settings.KeywordsText, false);
            ApplyInteractionState();
            return;
        }

        if (loadOnlyWhenEditorEmpty && !string.IsNullOrWhiteSpace(_keywordsEditor.Text))
        {
            return;
        }

        if (!File.Exists(_settings.KeywordsSource))
        {
            if (logIfMissing)
            {
                AppendStatus(StatusSeverity.Warning, $"Keywords source file was not found: {_settings.KeywordsSource}");
            }

            return;
        }

        var rawContent = await File.ReadAllTextAsync(_settings.KeywordsSource);
        var keywordAnalysis = KeywordBatchPlanner.AnalyzeKeywords(GetKeywordsTextForProcessing(rawContent), _settings.Delimiter);
        _settings.KeywordsText = rawContent;
        ApplyKeywordEditorContent(rawContent, true, keywordAnalysis);
        LogKeywordAnalysis("File load", keywordAnalysis);
        _propertyGrid.Refresh();
        ApplyInteractionState();
        ScheduleSave();
        AppendStatus(
            StatusSeverity.Info,
            _settings.SkipFirstRow
            ? $"Loaded keywords from file: {_settings.KeywordsSource}. The first row will be skipped during processing."
                : $"Loaded keywords from file: {_settings.KeywordsSource}");
    }

    private Task ReevaluateKeywordStateAsync()
    {
        if (_settings.IsKeywordFileMode)
        {
            var keywordAnalysis = KeywordBatchPlanner.AnalyzeKeywords(GetKeywordsTextForProcessing(), _settings.Delimiter);
            ApplyKeywordEditorContent(_settings.KeywordsText, true, keywordAnalysis);
            LogKeywordAnalysis("Keyword count recalculated", keywordAnalysis);
        }
        else if (!string.IsNullOrWhiteSpace(_settings.KeywordsText))
        {
            LogKeywordAnalysis("Keyword count recalculated", KeywordBatchPlanner.AnalyzeKeywords(GetKeywordsTextForProcessing(), _settings.Delimiter));
        }

        return Task.CompletedTask;
    }

    private static string RemoveFirstLine(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var newlineIndex = content.IndexOfAny(['\r', '\n']);
        if (newlineIndex < 0)
        {
            return string.Empty;
        }

        var contentStartIndex = newlineIndex + 1;
        if (content[newlineIndex] == '\r' && contentStartIndex < content.Length && content[contentStartIndex] == '\n')
        {
            contentStartIndex++;
        }

        return content[contentStartIndex..];
    }

    private string GetKeywordsTextForProcessing()
    {
        return GetKeywordsTextForProcessing(_settings.KeywordsText);
    }

    private string GetKeywordsTextForProcessing(string rawContent)
    {
        return _settings.SkipFirstRow
            ? RemoveFirstLine(rawContent)
            : rawContent;
    }

    private static string BuildQueryPreview(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return string.Empty;
        }

        var singleLineQuery = queryText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (singleLineQuery.Length <= StatusQueryPreviewLimit)
        {
            return singleLineQuery;
        }

        return $"{singleLineQuery[..StatusQueryPreviewLimit]}...";
    }

    private void CaptureEditorsIntoSettings()
    {
        _settings.SoqlTemplate = _soqlEditor.Text;

        if (!_settings.IsKeywordFileMode)
        {
            _settings.KeywordsText = _keywordsEditor.Text;
        }
    }

    private async Task InvalidateSessionStateAsync(string reason)
    {
        _sessionState = null;
        _hasSessionAuthentication = false;

        try
        {
            await _sessionStateStore.ClearAsync();
        }
        catch (Exception exception)
        {
            AppendStatus(StatusSeverity.Warning, $"Could not clear stored session file: {exception.Message}");
        }

        AppendStatus(StatusSeverity.Warning, reason);
        _propertyGrid.Refresh();
        ApplyInteractionState();
    }

    private void ScheduleSave()
    {
        if (_suppressSaveSchedule)
        {
            return;
        }

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private async void SaveDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _saveDebounceTimer.Stop();
        CaptureEditorsIntoSettings();

        try
        {
            await _settingsStore.SaveAsync(_settings);
            AppendStatus(StatusSeverity.Configuration, "Configuration auto-saved.");
        }
        catch (Exception exception)
        {
            AppendStatus(StatusSeverity.Error, $"Configuration save failed: {exception.Message}");
        }
    }

    private void ApplyInteractionState(bool isRunning = false, bool isLoginInProgress = false)
    {
        _propertyGrid.Enabled = !isRunning;
        _soqlEditor.ReadOnly = isRunning;
        _keywordsEditor.ReadOnly = isRunning || _settings.IsKeywordFileMode;
        _flushButton.Enabled = !isRunning;
        // Login button: enabled in idle session mode, or while login is in progress (to allow stopping).
        _loginButton.Enabled = (!isRunning && _settings.LoginMethod == LoginMethod.Session) || isLoginInProgress;
        _loginButton.Text = isLoginInProgress ? "Stop" : "Login";
        // During login the Run button must be disabled entirely; it becomes Cancel only during an actual extraction run.
        _runButton.Enabled = !isLoginInProgress;
        _runButton.Text = (isRunning && !isLoginInProgress) ? "Cancel" : "Run";
        _keywordModeLabel.Text = _settings.IsKeywordFileMode ? "Keyword Source Mode: File" : "Keyword Source Mode: Direct Paste";
        _loginModeLabel.Text = _settings.LoginMethod == LoginMethod.Session
            ? (_hasSessionAuthentication
                ? $"Login Mode: Session (authenticated: {_sessionState?.InstanceUrl ?? "unknown"})"
                : "Login Mode: Session (not authenticated)")
            : "Login Mode: Password";
        _outputPathLabel.Text = string.IsNullOrWhiteSpace(_settings.OutputFilePath)
            ? "Output File: Not selected"
            : $"Output File: {_settings.OutputFilePath}";
        _callbackUrlLabel.Text = $"Callback URL: http://localhost:{_settings.CallbackPort}/callback/";
    }

    private Task<bool> PromptForOutputFilePathAsync()
    {
        if (HasReusableOutputFilePath())
        {
            AppendStatus(StatusSeverity.Configuration, $"Reusing output file: {_settings.OutputFilePath}");
            ApplyInteractionState();
            return Task.FromResult(true);
        }

        var initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var initialFileName = BuildDefaultOutputFileName();

        if (!string.IsNullOrWhiteSpace(_settings.OutputFilePath))
        {
            var configuredDirectory = Path.GetDirectoryName(_settings.OutputFilePath);
            if (!string.IsNullOrWhiteSpace(configuredDirectory) && Directory.Exists(configuredDirectory))
            {
                initialDirectory = configuredDirectory;
            }

            var configuredFileName = Path.GetFileName(_settings.OutputFilePath);
            if (!string.IsNullOrWhiteSpace(configuredFileName))
            {
                initialFileName = configuredFileName;
            }
        }

        ApplyInteractionState();

        using var dialog = new SaveFileDialog
        {
            Title = "Select output file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            FileName = initialFileName,
            InitialDirectory = initialDirectory,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return Task.FromResult(false);
        }

        _settings.OutputFilePath = dialog.FileName;
        _propertyGrid.Refresh();
        ApplyInteractionState();

        AppendStatus(StatusSeverity.Configuration, $"Output file selected: {_settings.OutputFilePath}");

        return Task.FromResult(true);
    }

    private bool HasReusableOutputFilePath()
    {
        if (string.IsNullOrWhiteSpace(_settings.OutputFilePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(_settings.OutputFilePath);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    private static string BuildDefaultOutputFileName()
    {
        return $"extract_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    }

    private void ApplyKeywordEditorContent(string content, bool isFileMode, KeywordAnalysis? keywordAnalysis = null)
    {
        _suppressSaveSchedule = true;
        _keywordsEditor.Text = isFileMode ? BuildKeywordPreview(content, keywordAnalysis) : content;
        _suppressSaveSchedule = false;
    }

    private static string BuildKeywordPreview(string content, KeywordAnalysis? keywordAnalysis)
    {
        if (content.Length <= KeywordPreviewLimit)
        {
            return content;
        }

        var loadedItemCount = keywordAnalysis is null
            ? 0
            : Math.Max(keywordAnalysis.InputCount - keywordAnalysis.IgnoredCount, 0);

        return $"{content[..KeywordPreviewLimit]} ... (loaded {loadedItemCount} items)";
    }

    private void LogKeywordAnalysis(string sourceLabel, KeywordAnalysis analysis)
    {
        var signature = $"{sourceLabel}:{analysis.InputCount}:{analysis.UniqueCount}:{analysis.DuplicateCount}:{analysis.IgnoredCount}:{_settings.IsKeywordFileMode}";
        if (string.Equals(signature, _lastKeywordLogSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastKeywordLogSignature = signature;
        var loadedItemCount = Math.Max(analysis.InputCount - analysis.IgnoredCount, 0);
        AppendStatus(
            StatusSeverity.Info,
            $"{sourceLabel} keywords detected: {loadedItemCount} items. Unique={analysis.UniqueCount}, Duplicates={analysis.DuplicateCount}, Ignored={analysis.IgnoredCount}.");
    }

    private void AppendStatus(StatusSeverity severity, string message)
    {
        var color = severity switch
        {
            StatusSeverity.Info => Color.Black,
            StatusSeverity.Progress => Color.Teal,
            StatusSeverity.Warning => Color.DarkOrange,
            StatusSeverity.Error => Color.Firebrick,
            StatusSeverity.Validation => Color.MediumBlue,
            StatusSeverity.Configuration => Color.DarkSlateBlue,
            _ => Color.Black,
        };

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _statusView.SelectionStart = _statusView.TextLength;
        _statusView.SelectionLength = 0;
        _statusView.SelectionColor = color;
        _statusView.AppendText($"[{timestamp}] [{severity}] {message}{Environment.NewLine}");
        _statusView.SelectionColor = _statusView.ForeColor;
        _statusView.ScrollToCaret();
    }

    private enum StatusSeverity
    {
        Info,
        Progress,
        Warning,
        Error,
        Validation,
        Configuration,
    }

    private sealed record ExecutionRunSummary(
        int SuccessfulBatches,
        int FailedBatches,
        int AggregatedRecordCount);
}