-- Small key/value config table so dbo.AskProductQuestion (see
-- dbo.AskProductQuestion.sql) doesn't have the Azure OpenAI resource name,
-- chat deployment, and API version baked into its own text - the same
-- three values already live in Web.config's <appSettings> for the C#
-- pipeline (AzureOpenAiEndpoint/AzureOpenAiChatDeployment/
-- AzureOpenAiApiVersion); this just gives the stored-procedure pipeline
-- the same "edit config, not code" story instead of requiring a
-- CREATE OR ALTER PROCEDURE every time one of these changes.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppConfig' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AppConfig (
        ConfigKey NVARCHAR(100) NOT NULL PRIMARY KEY,
        ConfigValue NVARCHAR(500) NOT NULL
    );
END
GO

-- Seeds placeholders (the AzureOpenAiEndpoint one matches Web.config's own
-- placeholder) rather than a real endpoint - only inserts rows that don't
-- already exist, so re-running this script after pulling a fresh copy of
-- the repo never clobbers a real value you've already set. Before using
-- dbo.AskProductQuestion for real:
--   UPDATE dbo.AppConfig SET ConfigValue = 'https://<your-resource>.openai.azure.com'
--   WHERE ConfigKey = 'AzureOpenAiEndpoint';
-- using the same resource URL the DATABASE SCOPED CREDENTIAL for it was
-- registered under.
MERGE dbo.AppConfig AS target
USING (VALUES
    ('AzureOpenAiEndpoint', 'https://your-openai-resource.openai.azure.com'),
    ('AzureOpenAiChatDeployment', 'gpt-5.4-mini'),
    ('AzureOpenAiApiVersion', '2024-10-21')
) AS source (ConfigKey, ConfigValue)
ON target.ConfigKey = source.ConfigKey
WHEN NOT MATCHED THEN
    INSERT (ConfigKey, ConfigValue) VALUES (source.ConfigKey, source.ConfigValue);
GO
