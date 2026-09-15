CREATE TABLE dbo.CallLog (
    CallLogId INT IDENTITY(1,1) PRIMARY KEY,
    CallTimestamp DATETIME2 NOT NULL,
    Mode NVARCHAR(20) NOT NULL,
    Question NVARCHAR(1000) NOT NULL,
    MaxCompletionTokens INT NOT NULL,
    EstimatedInputTokens INT NULL,
    EstimatedOutputTokens INT NULL,
    Success BIT NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL
);
