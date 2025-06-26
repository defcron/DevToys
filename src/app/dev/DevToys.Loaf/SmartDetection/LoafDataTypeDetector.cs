using DevToys.Api;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;

namespace DevToys.Loaf.SmartDetection;

[Export(typeof(IDataTypeDetector))]
[DataTypeName(InternalName, baseName: PredefinedCommonDataTypeNames.Text)]
internal sealed partial class LoafDataTypeDetector : IDataTypeDetector
{
    internal const string InternalName = "Loaf";
    
    [GeneratedRegex(@"^SHA256\(-\)=[0-9a-f]{64} [0-9a-f]*$", 
    RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LoafFormatRegex();

    public ValueTask<DataDetectionResult> TryDetectDataAsync(
        object data, 
        DataDetectionResult? resultFromBaseDetector, 
        CancellationToken cancellationToken)
    {
        if (resultFromBaseDetector?.Data is string { Length: > 0 } dataString)
        {
            // Check if the string matches .loaf format
            if (LoafFormatRegex().IsMatch(dataString.Trim()))
            {
                return ValueTask.FromResult(new DataDetectionResult(Success: true, Data: dataString));
            }
        }

        return ValueTask.FromResult(DataDetectionResult.Unsuccessful);
    }
}