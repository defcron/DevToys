# DevToys.Loaf Extension

## Overview

This extension adds support for the LoaF (Linear Object Archive Format) to DevToys, providing encoding, decoding, and verification capabilities for .loaf files. The LoaF format is a delightfully simple, single-line, self-validating archive format perfect for command-line usage and text-based transmission.

## Features

### Core Operations
- **Create**: Convert text content into .loaf archive format
- **Verify**: Validate .loaf file integrity using embedded SHA256 checksum
- **Extract**: Decode and extract contents from .loaf files

### Technical Capabilities
- Full compliance with the official .loaf format specification
- Smart detection of .loaf files for automatic tool selection
- Real-time processing with async operations
- Comprehensive error handling and user feedback
- Integration with DevToys "Encoders / Decoders" section

## .loaf Format Specification

The LoaF format follows this structure:
```
SHA256(-)=<64-hex-hash> <hex-encoded-gzipped-tar-data>
```

### Format Process
1. **Archive**: Content is packaged using TAR format
2. **Compress**: TAR archive is compressed using GZip
3. **Encode**: Compressed data is hex-encoded
4. **Checksum**: SHA256 hash is calculated and prepended

## Implementation Details

### Project Structure
```
DevToys.Loaf/
├── DevToys.Loaf.csproj           # Project configuration
├── DevToysLoafResourceAssemblyIdentifier.cs
├── Helpers/
│   └── LoafHelper.cs             # Core .loaf format logic
├── SmartDetection/
│   └── LoafDataTypeDetector.cs   # Smart detection for .loaf files
├── Tools/LoafTool/
│   ├── LoafTool.resx            # Localized strings
│   ├── LoafTool.Designer.cs     # Generated resource accessor
│   └── LoafToolGuiTool.cs       # Main UI tool implementation
└── Resources/
    └── DevToysLoaf.resx         # Base resources
```

### Key Components

#### LoafHelper
Core utility class providing:
- `CreateLoafAsync()`: Creates .loaf archives from text input
- `VerifyLoafAsync()`: Validates .loaf file integrity  
- `ExtractLoafAsync()`: Extracts content from .loaf files

#### LoafDataTypeDetector
Smart detection component that:
- Uses regex pattern matching to identify .loaf format
- Integrates with DevToys smart detection system
- Enables automatic tool selection

#### LoafToolGuiTool
Main UI component featuring:
- Split-panel interface (input/output)
- Operation mode selector (Create/Verify/Extract)
- Real-time processing with async operations
- Settings persistence

## Usage

### Creating a .loaf Archive
1. Select "Create LoaF" operation mode
2. Enter text content in the input area
3. View the generated .loaf archive in the output area

### Verifying a .loaf Archive
1. Select "Verify LoaF" operation mode
2. Paste .loaf content in the input area
3. See verification result in the output area

### Extracting from a .loaf Archive
1. Select "Extract LoaF" operation mode
2. Paste .loaf content in the input area
3. View extracted files and content in the output area

## Integration

The extension integrates seamlessly with DevToys:
- Appears in the "Encoders / Decoders" section
- Supports smart detection for automatic tool selection
- Follows DevToys UI and UX patterns
- Uses DevToys resource management system

## Testing

Unit tests are provided in `DevToys.UnitTests/Loaf/LoafHelperTests.cs` covering:
- Create/verify/extract operations
- Error handling scenarios
- Data type detection functionality
- Edge cases and format validation

## Requirements

- .NET 8.0
- DevToys.Api framework
- OneOf library for union types

## Build

The extension builds as part of the DevToys solution:
```bash
dotnet build src/app/dev/DevToys.Loaf/DevToys.Loaf.csproj
```

## License

This implementation follows the DevToys project licensing and includes attribution to the original .loaf format specification.