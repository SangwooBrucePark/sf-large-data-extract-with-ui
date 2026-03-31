using System.ComponentModel;
using System.Drawing.Design;
using LargeDataExportWithUI.App.Models;

namespace LargeDataExportWithUI.App.Services;

public sealed class ExtractionMethodConverter : EnumConverter
{
    private static readonly IReadOnlyDictionary<ExtractionMethod, string> DisplayNames = new Dictionary<ExtractionMethod, string>
    {
        [ExtractionMethod.BulkApi2] = "Bulk API 2.0",
        [ExtractionMethod.RestQuery] = "REST Query",
        [ExtractionMethod.RestQueryAll] = "REST QueryAll",
    };

    public ExtractionMethodConverter() : base(typeof(ExtractionMethod))
    {
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(Enum.GetValues<ExtractionMethod>());
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is ExtractionMethod method && DisplayNames.TryGetValue(method, out var displayName))
        {
            return displayName;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string displayName)
        {
            var match = DisplayNames.FirstOrDefault(pair => string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase));
            if (!EqualityComparer<KeyValuePair<ExtractionMethod, string>>.Default.Equals(match, default))
            {
                return match.Key;
            }
        }

        return base.ConvertFrom(context, culture, value);
    }

    public static string ToDisplayName(ExtractionMethod method)
    {
        return DisplayNames.TryGetValue(method, out var displayName)
            ? displayName
            : method.ToString();
    }
}

public sealed class CredentialValueSourceConverter : EnumConverter
{
    private static readonly IReadOnlyDictionary<CredentialValueSource, string> DisplayNames = new Dictionary<CredentialValueSource, string>
    {
        [CredentialValueSource.Custom] = "Custom",
        [CredentialValueSource.EnvironmentVariable] = "Env Var",
    };

    public CredentialValueSourceConverter() : base(typeof(CredentialValueSource))
    {
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(Enum.GetValues<CredentialValueSource>());
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is CredentialValueSource source && DisplayNames.TryGetValue(source, out var displayName))
        {
            return displayName;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string displayName)
        {
            var match = DisplayNames.FirstOrDefault(pair => string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase));
            if (!EqualityComparer<KeyValuePair<CredentialValueSource, string>>.Default.Equals(match, default))
            {
                return match.Key;
            }
        }

        return base.ConvertFrom(context, culture, value);
    }
}

public sealed class ConfigurablePropertyDescriptor : PropertyDescriptor
{
    private readonly PropertyDescriptor _innerProperty;
    private readonly bool _isReadOnly;
    private readonly string _displayName;
    private readonly string _description;
    private readonly AttributeCollection? _attributes;

    public ConfigurablePropertyDescriptor(
        PropertyDescriptor innerProperty,
        bool isReadOnly,
        string? displayName = null,
        string? description = null,
        AttributeCollection? attributes = null)
        : base(innerProperty)
    {
        _innerProperty = innerProperty;
        _isReadOnly = isReadOnly;
        _displayName = displayName ?? innerProperty.DisplayName;
        _description = description ?? innerProperty.Description;
        _attributes = attributes;
    }

    public override AttributeCollection Attributes => _attributes ?? _innerProperty.Attributes;

    public override bool CanResetValue(object component) => _innerProperty.CanResetValue(component);

    public override Type ComponentType => _innerProperty.ComponentType;

    public override object? GetValue(object? component) => _innerProperty.GetValue(component);

    public override bool IsReadOnly => _isReadOnly;

    public override Type PropertyType => _innerProperty.PropertyType;

    public override void ResetValue(object component) => _innerProperty.ResetValue(component);

    public override void SetValue(object? component, object? value) => _innerProperty.SetValue(component, value);

    public override bool ShouldSerializeValue(object component) => _innerProperty.ShouldSerializeValue(component);

    public override string Category => _innerProperty.Category;

    public override string Description => _description;

    public override string DisplayName => _displayName;
}

public sealed class KeywordsSourceFileEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
    {
        return UITypeEditorEditStyle.Modal;
    }

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select keywords source file",
            Filter = "Text and CSV files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (value is string existingPath && !string.IsNullOrWhiteSpace(existingPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(existingPath);
            dialog.FileName = Path.GetFileName(existingPath);
        }

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : value;
    }
}

public sealed class KeywordValueTypeConverter : EnumConverter
{
    private static readonly IReadOnlyDictionary<KeywordValueType, string> DisplayNames = new Dictionary<KeywordValueType, string>
    {
        [KeywordValueType.Text] = "Text",
        [KeywordValueType.Number] = "Number",
    };

    public KeywordValueTypeConverter() : base(typeof(KeywordValueType))
    {
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(Enum.GetValues<KeywordValueType>());
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is KeywordValueType type && DisplayNames.TryGetValue(type, out var displayName))
        {
            return displayName;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string displayName)
        {
            var match = DisplayNames.FirstOrDefault(pair => string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase));
            if (!EqualityComparer<KeyValuePair<KeywordValueType, string>>.Default.Equals(match, default))
            {
                return match.Key;
            }
        }

        return base.ConvertFrom(context, culture, value);
    }
}