using System.ComponentModel;
using System.Drawing.Design;
using System.Text.Json.Serialization;
using LargeDataExportWithUI.App.Services;

namespace LargeDataExportWithUI.App.Models;

public sealed class AppSettings : ICustomTypeDescriptor
{
    private CredentialValueSource _credentialValueMode = CredentialValueSource.Custom;

    [Category("Authentication")]
    [DisplayName("Login Method")]
    public LoginMethod LoginMethod { get; set; } = LoginMethod.Password;

    [Category("Authentication")]
    [DisplayName("Org Type")]
    public OrgType OrgType { get; set; } = OrgType.Production;

    [Category("Authentication")]
    [DisplayName("Login URL")]
    public string LoginUrl { get; set; } = GetDefaultLoginUrl(OrgType.Production);

    [Category("Authentication")]
    [DisplayName("Credential Source")]
    [TypeConverter(typeof(CredentialValueSourceConverter))]
    public CredentialValueSource CredentialValueMode
    {
        get => _credentialValueMode;
        set
        {
            _credentialValueMode = value;
            UsernameValueMode = value;
            PasswordValueMode = value;
            SecurityTokenValueMode = value;
            ClientIdValueMode = value;
        }
    }

    [Browsable(false)]
    public CredentialValueSource UsernameValueMode { get; set; } = CredentialValueSource.Custom;

    [Category("Authentication")]
    [DisplayName("Username")]
    public string Username { get; set; } = string.Empty;

    [Browsable(false)]
    public CredentialValueSource PasswordValueMode { get; set; } = CredentialValueSource.Custom;

    [Category("Authentication")]
    [DisplayName("Password")]
    public string Password { get; set; } = string.Empty;

    [Browsable(false)]
    public CredentialValueSource SecurityTokenValueMode { get; set; } = CredentialValueSource.Custom;

    [Category("Authentication")]
    [DisplayName("Security Token")]
    public string SecurityToken { get; set; } = string.Empty;

    [Browsable(false)]
    public CredentialValueSource ClientIdValueMode { get; set; } = CredentialValueSource.Custom;

    [Category("Authentication")]
    [DisplayName("Client ID")]
    public string ClientId { get; set; } = string.Empty;

    [Category("Authentication")]
    [DisplayName("Callback Port")]
    public int CallbackPort { get; set; } = 17171;

    [Category("Query Input")]
    [DisplayName("IN Clause Token")]
    public string InClauseToken { get; set; } = "__IN_CLAUSE__";

    [Category("Query Input")]
    [DisplayName("Keywords Source")]
    [Editor(typeof(KeywordsSourceFileEditor), typeof(UITypeEditor))]
    [JsonIgnore]
    public string KeywordsSource { get; set; } = string.Empty;

    [Category("Query Input")]
    [DisplayName("Delimiter")]
    public string Delimiter { get; set; } = ",";

    [Category("Query Input")]
    [DisplayName("Skip First Row")]
    public bool SkipFirstRow { get; set; }

    [Category("Query Input")]
    [DisplayName("Keyword Value Type")]
    [TypeConverter(typeof(KeywordValueTypeConverter))]
    public KeywordValueType KeywordValueType { get; set; } = KeywordValueType.Text;

    [Category("Execution")]
    [DisplayName("Extraction Method")]
    [TypeConverter(typeof(ExtractionMethodConverter))]
    public ExtractionMethod ExtractionMethod { get; set; } = ExtractionMethod.BulkApi2;

    [Category("Execution")]
    [DisplayName("Chunk Size")]
    public int ChunkSize { get; set; } = 500;

    [Browsable(false)]
    [JsonIgnore]
    public string SoqlTemplate { get; set; } = string.Empty;

    [Browsable(false)]
    [JsonIgnore]
    public string KeywordsText { get; set; } = string.Empty;

    [Browsable(false)]
    [JsonIgnore]
    public string OutputFilePath { get; set; } = string.Empty;

    [Browsable(false)]
    [JsonIgnore]
    public bool IsKeywordFileMode => !string.IsNullOrWhiteSpace(KeywordsSource);

    public void EnsureDefaults()
    {
        NormalizeCredentialMode();

        if (string.IsNullOrWhiteSpace(LoginUrl))
        {
            LoginUrl = GetDefaultLoginUrl(OrgType);
        }

        if (string.IsNullOrWhiteSpace(InClauseToken))
        {
            InClauseToken = "__IN_CLAUSE__";
        }

        if (string.IsNullOrWhiteSpace(Delimiter))
        {
            Delimiter = ",";
        }

        if (ChunkSize <= 0)
        {
            ChunkSize = 500;
        }

        if (CallbackPort <= 0)
        {
            CallbackPort = 17171;
        }
    }

    private void NormalizeCredentialMode()
    {
        var usernameNormalization = NormalizeCredentialValue(UsernameValueMode, Username);
        Username = usernameNormalization.Value;

        var passwordNormalization = NormalizeCredentialValue(PasswordValueMode, Password);
        Password = passwordNormalization.Value;

        var tokenNormalization = NormalizeCredentialValue(SecurityTokenValueMode, SecurityToken);
        SecurityToken = tokenNormalization.Value;

        var clientIdNormalization = NormalizeCredentialValue(ClientIdValueMode, ClientId);
        ClientId = clientIdNormalization.Value;

        CredentialValueMode = ResolveCredentialMode(
            CredentialValueMode,
            usernameNormalization.Mode,
            passwordNormalization.Mode,
            tokenNormalization.Mode,
            clientIdNormalization.Mode);

        SyncLegacyCredentialModes();
    }

    private void SyncLegacyCredentialModes()
    {
        UsernameValueMode = CredentialValueMode;
        PasswordValueMode = CredentialValueMode;
        SecurityTokenValueMode = CredentialValueMode;
        ClientIdValueMode = CredentialValueMode;
    }

    private static CredentialValueSource ResolveCredentialMode(
        CredentialValueSource currentMode,
        params CredentialValueSource[] migratedModes)
    {
        if (migratedModes.Any(mode => mode == CredentialValueSource.EnvironmentVariable))
        {
            return CredentialValueSource.EnvironmentVariable;
        }

        return currentMode;
    }

    private static (CredentialValueSource Mode, string Value) NormalizeCredentialValue(CredentialValueSource mode, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (mode, string.Empty);
        }

        const string environmentPrefix = "env:";
        if (!value.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (mode, value);
        }

        var variableName = value[environmentPrefix.Length..].Trim();
        return (CredentialValueSource.EnvironmentVariable, variableName);
    }

    public static string GetDefaultLoginUrl(OrgType orgType)
    {
        return orgType == OrgType.Sandbox
            ? "https://test.salesforce.com"
            : "https://login.salesforce.com";
    }

    AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);

    string? ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);

    string? ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);

    TypeConverter? ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);

    EventDescriptor? ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);

    PropertyDescriptor? ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);

    object? ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[]? attributes) => TypeDescriptor.GetEvents(this, attributes, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() => ((ICustomTypeDescriptor)this).GetProperties(null);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[]? attributes)
    {
        var properties = TypeDescriptor.GetProperties(this, attributes, true);
        var wrappedProperties = new List<PropertyDescriptor>(properties.Count);

        foreach (PropertyDescriptor property in properties)
        {
            var isReadOnly = property.IsReadOnly;
            var displayName = property.DisplayName;
            var description = property.Description;
            AttributeCollection? overriddenAttributes = null;

            if ((property.Name == nameof(Username)
                    || property.Name == nameof(Password)
                    || property.Name == nameof(SecurityToken)
                    )
                && LoginMethod != LoginMethod.Password)
            {
                isReadOnly = true;
            }

            if ((property.Name == nameof(ClientId)
                    || property.Name == nameof(CallbackPort))
                && LoginMethod != LoginMethod.Session)
            {
                isReadOnly = true;
            }

            if (property.Name == nameof(Username))
            {
                displayName = CredentialValueMode == CredentialValueSource.EnvironmentVariable ? "Username Variable Name" : "Username";
                description = CredentialValueMode == CredentialValueSource.EnvironmentVariable
                    ? "Environment variable name that contains the Salesforce username."
                    : "Salesforce username used for password authentication.";
            }

            if (property.Name == nameof(Password))
            {
                var isEnvironmentVariable = CredentialValueMode == CredentialValueSource.EnvironmentVariable;
                displayName = isEnvironmentVariable ? "Password Variable Name" : "Password";
                description = isEnvironmentVariable
                    ? "Environment variable name that contains the Salesforce password."
                    : "Salesforce password used for password authentication.";
                overriddenAttributes = OverrideAttributes(property, new PasswordPropertyTextAttribute(!isEnvironmentVariable));
            }

            if (property.Name == nameof(SecurityToken))
            {
                var isEnvironmentVariable = CredentialValueMode == CredentialValueSource.EnvironmentVariable;
                displayName = isEnvironmentVariable ? "Security Token Variable Name" : "Security Token";
                description = isEnvironmentVariable
                    ? "Environment variable name that contains the Salesforce security token."
                    : "Salesforce security token appended to the password for password authentication.";
                overriddenAttributes = OverrideAttributes(property, new PasswordPropertyTextAttribute(!isEnvironmentVariable));
            }

            if (property.Name == nameof(ClientId))
            {
                displayName = CredentialValueMode == CredentialValueSource.EnvironmentVariable ? "Client ID Variable Name" : "Client ID";
                description = CredentialValueMode == CredentialValueSource.EnvironmentVariable
                    ? "Environment variable name that contains the Connected App client ID."
                    : "Connected App client ID used for session login.";
            }

            if (property.Name == nameof(CredentialValueMode))
            {
                description = "Choose whether all authentication credentials are entered directly or read from environment variables.";
            }

            if (property.IsReadOnly == isReadOnly
                && string.Equals(property.DisplayName, displayName, StringComparison.Ordinal)
                && string.Equals(property.Description, description, StringComparison.Ordinal)
                && overriddenAttributes is null)
            {
                wrappedProperties.Add(property);
                continue;
            }

            wrappedProperties.Add(new ConfigurablePropertyDescriptor(property, isReadOnly, displayName, description, overriddenAttributes));
        }

        return new PropertyDescriptorCollection([.. wrappedProperties], true);
    }

    private static AttributeCollection OverrideAttributes(PropertyDescriptor property, params Attribute[] overrides)
    {
        var attributes = new List<Attribute>();

        foreach (Attribute attribute in property.Attributes)
        {
            attributes.Add(attribute);
        }

        foreach (var attribute in overrides)
        {
            var replacementIndex = -1;
            for (var index = 0; index < attributes.Count; index++)
            {
                if (attributes[index].GetType() == attribute.GetType())
                {
                    replacementIndex = index;
                    break;
                }
            }

            if (replacementIndex >= 0)
            {
                attributes[replacementIndex] = attribute;
            }
            else
            {
                attributes.Add(attribute);
            }
        }

        return new AttributeCollection([.. attributes]);
    }

    object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor? pd) => this;
}

public enum LoginMethod
{
    Password,
    Session,
}

public enum OrgType
{
    Production,
    Sandbox,
}

public enum ExtractionMethod
{
    BulkApi2,
    RestQuery,
    RestQueryAll,
}

public enum CredentialValueSource
{
    Custom,
    EnvironmentVariable,
}

public enum KeywordValueType
{
    Text,
    Number,
}