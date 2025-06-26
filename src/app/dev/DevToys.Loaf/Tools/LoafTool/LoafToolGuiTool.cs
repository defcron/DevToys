using DevToys.Api;
using DevToys.Loaf.Helpers;
using DevToys.Loaf.SmartDetection;
using Microsoft.Extensions.Logging;
using OneOf;
using System.ComponentModel.Composition;
using System.Text;
using static DevToys.Api.GUI;

namespace DevToys.Loaf.Tools.LoafTool;

[Export(typeof(IGuiTool))]
[Name("LoaF")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF156', // Archive icon
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(DevToysLoafResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Loaf.Tools.LoafTool.LoafTool",
    ShortDisplayTitleResourceName = nameof(LoafTool.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(LoafTool.LongDisplayTitle),
    DescriptionResourceName = nameof(LoafTool.Description),
    AccessibleNameResourceName = nameof(LoafTool.AccessibleName))]
[AcceptedDataTypeName(LoafDataTypeDetector.InternalName)]
[AcceptedDataTypeName(PredefinedCommonDataTypeNames.Text)]
public sealed class LoafToolGuiTool : IGuiTool, IDisposable
{
    private enum LoafOperation
    {
        Create,
        Verify,
        Extract
    }

    private static readonly SettingDefinition<LoafOperation> _operationMode
        = new(name: $"{nameof(LoafToolGuiTool)}.{nameof(_operationMode)}", defaultValue: LoafOperation.Create);

    private enum GridColumn
    {
        Content
    }

    private enum GridRow
    {
        Header,
        Content
    }

    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IFileStorage _fileStorage;
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("loaf-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("loaf-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public LoafToolGuiTool(ISettingsProvider settingsProvider, IFileStorage fileStorage)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;
        _fileStorage = fileStorage;
        
        UpdatePlaceholderAndTitle();
    }

    internal Task? WorkTask { get; private set; }

    public UIToolView View
        => new(
            isScrollable: true,
            Grid()
                .ColumnLargeSpacing()
                .RowLargeSpacing()
                .Rows(
                    (GridRow.Header, Auto),
                    (GridRow.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
                .Columns(
                    (GridColumn.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
                .Cells(
                    Cell(
                        GridRow.Header,
                        GridColumn.Content,
                        Stack().Vertical().WithChildren(
                            Label()
                                .Text(LoafTool.Configuration),
                            Setting("loaf-operation-setting")
                                .Icon("FluentSystemIcons", '\uF18D')
                                .Title(LoafTool.OperationTitle)
                                .Description(LoafTool.OperationDescription)
                                .Handle(
                                    _settingsProvider,
                                    _operationMode,
                                    OnOperationModeChanged,
                                    Item(LoafTool.CreateLoaf, LoafOperation.Create),
                                    Item(LoafTool.VerifyLoaf, LoafOperation.Verify),
                                    Item(LoafTool.ExtractLoaf, LoafOperation.Extract)
                                )
                        )
                    ),
                    Cell(
                        GridRow.Content,
                        GridColumn.Content,
                        SplitGrid()
                            .Vertical()
                            .WithLeftPaneChild(
                                _inputTextArea
                                    .Title(LoafTool.Input)
                                    .OnTextChanged(OnInputTextChanged))
                            .WithRightPaneChild(
                                _outputTextArea
                                    .Title(LoafTool.Output)
                                    .ReadOnly()
                                    .Extendable())
                    )
                )
        );

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (parsedData is string data)
        {
            if (dataTypeName == LoafDataTypeDetector.InternalName)
            {
                // It's a .loaf file, set to verify mode
                _settingsProvider.SetSetting(_operationMode, LoafOperation.Verify);
                UpdatePlaceholderAndTitle();
                _inputTextArea.Text(data);
            }
            else if (dataTypeName == PredefinedCommonDataTypeNames.Text)
            {
                // It's text content, set to create mode
                _settingsProvider.SetSetting(_operationMode, LoafOperation.Create);
                UpdatePlaceholderAndTitle();
                _inputTextArea.Text(data);
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private void OnOperationModeChanged(LoafOperation operationMode)
    {
        UpdatePlaceholderAndTitle();
        StartOperation(_inputTextArea.Text);
    }

    private void OnInputTextChanged(string text)
    {
        StartOperation(text);
    }

    private void UpdatePlaceholderAndTitle()
    {
        // Note: DevToys API doesn't support placeholders for MultiLineTextInput
        // The operation mode is managed through the UI settings instead
    }

    private void StartOperation(string text)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        LoafOperation operation = _settingsProvider.GetSetting(_operationMode);
        WorkTask = PerformOperationAsync(text, operation, _cancellationTokenSource.Token);
    }

    private async Task PerformOperationAsync(string input, LoafOperation operation, CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            try
            {
                switch (operation)
                {
                    case LoafOperation.Create:
                        await CreateLoafAsync(input, cancellationToken);
                        break;
                        
                    case LoafOperation.Verify:
                        await VerifyLoafAsync(input, cancellationToken);
                        break;
                        
                    case LoafOperation.Extract:
                        await ExtractLoafAsync(input, cancellationToken);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing LoaF operation");
                _outputTextArea.Text($"❌ Error: {ex.Message}");
            }
        }
    }

    private async Task CreateLoafAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _outputTextArea.Text(string.Empty);
            return;
        }

        ResultInfo<string> result = await LoafHelper.CreateLoafAsync(
            OneOf<FileInfo, string>.FromT1(input),
            _fileStorage,
            _logger,
            cancellationToken);

        if (result.HasSucceeded)
        {
            _outputTextArea.Text(result.Data!);
        }
        else
        {
            _outputTextArea.Text(LoafTool.CreateFailed);
        }
    }

    private async Task VerifyLoafAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _outputTextArea.Text(string.Empty);
            return;
        }

        ResultInfo<bool> result = await LoafHelper.VerifyLoafAsync(input.Trim(), _logger, cancellationToken);

        if (result.HasSucceeded)
        {
            string message = result.Data! ? LoafTool.VerificationSuccess : LoafTool.VerificationFailed;
            _outputTextArea.Text(message);
        }
        else
        {
            _outputTextArea.Text(LoafTool.VerificationFailed);
        }
    }

    private async Task ExtractLoafAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _outputTextArea.Text(string.Empty);
            return;
        }

        ResultInfo<List<ExtractedFile>> result = await LoafHelper.ExtractLoafAsync(input.Trim(), _logger, cancellationToken);

        if (result.HasSucceeded)
        {
            var extractedFiles = result.Data!;
            if (extractedFiles.Count == 0)
            {
                _outputTextArea.Text("Empty archive");
            }
            else
            {
                var output = new StringBuilder();
                output.AppendLine(LoafTool.ExtractionSuccess);
                output.AppendLine();

                for (int i = 0; i < extractedFiles.Count; i++)
                {
                    var file = extractedFiles[i];
                    output.AppendLine($"📄 File: {file.Name}");
                    output.AppendLine($"   Size: {file.Data.Length} bytes");
                    
                    // Try to decode as text if it's reasonable size and looks like text
                    if (file.Data.Length <= 10000 && IsLikelyTextData(file.Data))
                    {
                        try
                        {
                            string textContent = System.Text.Encoding.UTF8.GetString(file.Data);
                            output.AppendLine("   Content:");
                            output.AppendLine(textContent);
                        }
                        catch
                        {
                            output.AppendLine("   Content: (binary data)");
                        }
                    }
                    else
                    {
                        output.AppendLine("   Content: (binary data)");
                    }

                    if (i < extractedFiles.Count - 1)
                    {
                        output.AppendLine();
                        output.AppendLine("---");
                        output.AppendLine();
                    }
                }

                _outputTextArea.Text(output.ToString());
            }
        }
        else
        {
            _outputTextArea.Text(LoafTool.ExtractionFailed);
        }
    }

    private static bool IsLikelyTextData(byte[] data)
    {
        if (data.Length == 0) return true;
        
        // Simple heuristic: check if most bytes are printable ASCII or common UTF-8
        int printableCount = 0;
        foreach (byte b in data)
        {
            if ((b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13) // Printable ASCII + tab/newline/CR
            {
                printableCount++;
            }
        }
        
        return (double)printableCount / data.Length > 0.8;
    }
}