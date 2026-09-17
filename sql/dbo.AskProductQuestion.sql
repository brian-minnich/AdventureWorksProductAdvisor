SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[AskProductQuestion]
    @Question NVARCHAR(1000),
    @MaxTokens INT = 500,
    @TopN INT = 5,
    -- Mirrors CSharpAskService's minSimilarityScore (AdventureWorksProductAdvisor/Services/CSharpAskService.cs):
    -- same 0.35 default, same check, same canned rejection message, so both
    -- pipelines apply the same guardrail. StoredProcAskService passes the
    -- same Web.config MinSimilarityScore value used by the C# pipeline,
    -- rather than this default drifting out of sync with that one by hand.
    @MinSimilarityScore FLOAT = 0.35,
    @Answer NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @questionVector VECTOR(1536);
    DECLARE @topDistance FLOAT;
    DECLARE @context NVARCHAR(MAX);
    DECLARE @payload NVARCHAR(MAX);
    DECLARE @response NVARCHAR(MAX);
    DECLARE @returnValue INT;

    -- Sourced from dbo.AppConfig (see sql/CreateAppConfigTable.sql) rather
    -- than hardcoded here, so deploying this procedure to a different
    -- environment - or changing the chat deployment/API version - is a
    -- one-row UPDATE to that table, not an edit to this procedure.
    -- @endpoint is SYSNAME (not NVARCHAR) because it's also passed straight
    -- through as @credential below - sp_invoke_external_rest_endpoint
    -- resolves that value to a DATABASE SCOPED CREDENTIAL at execution
    -- time, confirmed by running it directly against this database with
    -- @credential set from a variable.
    DECLARE @endpoint SYSNAME, @chatDeployment NVARCHAR(200), @apiVersion NVARCHAR(50);
    SELECT
        @endpoint = MAX(CASE WHEN ConfigKey = 'AzureOpenAiEndpoint' THEN ConfigValue END),
        @chatDeployment = MAX(CASE WHEN ConfigKey = 'AzureOpenAiChatDeployment' THEN ConfigValue END),
        @apiVersion = MAX(CASE WHEN ConfigKey = 'AzureOpenAiApiVersion' THEN ConfigValue END)
    FROM dbo.AppConfig
    WHERE ConfigKey IN ('AzureOpenAiEndpoint', 'AzureOpenAiChatDeployment', 'AzureOpenAiApiVersion');

    DECLARE @chatUrl NVARCHAR(1000) = @endpoint + N'/openai/deployments/' + @chatDeployment + N'/chat/completions?api-version=' + @apiVersion;

    -- Holds the joined, approximate-search results exactly once - both the
    -- off-topic-question threshold check below and the prompt context built
    -- afterward read from this same table, so they can never disagree
    -- about what the "best match" is (an earlier version of this
    -- guardrail ran a second, unjoined, non-approximate VECTOR_SEARCH just
    -- for the threshold check - doubling the search cost and risking a
    -- review that passes the threshold but doesn't survive these joins,
    -- e.g. an orphaned ProductID, leaving @context NULL further down).
    DECLARE @rankedReviews TABLE (
        ProductName NVARCHAR(MAX),
        ListPrice MONEY,
        Category NVARCHAR(MAX),
        Rating TINYINT,
        ReviewTitle NVARCHAR(MAX),
        ReviewText NVARCHAR(MAX),
        Distance FLOAT
    );

    -- Step 1: Convert the question to an embedding
    SELECT @questionVector = AI_GENERATE_EMBEDDINGS(@Question USE MODEL brianminnich_embedding_model);

    -- Step 2: Retrieve relevant reviews using ANN vector search, joined to
    -- their product info, together with the distance each was found at.
    INSERT INTO @rankedReviews (ProductName, ListPrice, Category, Rating, ReviewTitle, ReviewText, Distance)
    SELECT TOP (@TopN) WITH APPROXIMATE
        p.Name,
        p.ListPrice,
        pc.Name,
        r.Rating,
        r.ReviewTitle,
        r.ReviewText,
        vs.distance
    FROM VECTOR_SEARCH(
        TABLE = dbo.ProductReview AS r,
        COLUMN = ReviewVector,
        SIMILAR_TO = @questionVector,
        METRIC = 'cosine'
    ) AS vs
    INNER JOIN SalesLT.Product p
        ON r.ProductID = p.ProductID
    INNER JOIN SalesLT.ProductCategory pc
        ON p.ProductCategoryID = pc.ProductCategoryID
    ORDER BY vs.distance;

    -- Step 3: Check how close the single best-matching review is (the
    -- first row above, since it was inserted in ascending-distance order),
    -- before spending anything on a chat completion call - the same
    -- cost-guard idea as CSharpAskService's threshold check against
    -- SimilarityRanker's top score. VECTOR_SEARCH's cosine distance is
    -- 1 - cosine_similarity (0 = identical, 1 = orthogonal), so a smaller
    -- distance means a closer match.
    SELECT TOP (1) @topDistance = Distance FROM @rankedReviews ORDER BY Distance;

    IF @topDistance IS NULL
    BEGIN
        SET @Answer = 'No reviews found matching your query. Please try a different question.';
        RETURN;
    END

    IF (1.0 - @topDistance) < @MinSimilarityScore
    BEGIN
        SET @Answer = 'I can only answer questions about our products based on customer reviews. Please ask a product-related question.';
        RETURN;
    END

    -- Step 4: Build @context (what the chat model reads as "product
    -- reviews") from the same ranked reviews the threshold check above
    -- just approved, then the augmented prompt around it.
    SET @context = (
        SELECT ProductName, ListPrice, Category, Rating, ReviewTitle, ReviewText
        FROM @rankedReviews
        ORDER BY Distance
        FOR JSON PATH
    );

    SET @payload = JSON_OBJECT(
        'messages': JSON_ARRAY(
            JSON_OBJECT(
                'role': 'system',
                'content': 'You are an Adventure Works product assistant. Follow these rules:
1. Answer only using the provided product reviews and data
2. Reference specific customer experiences from the reviews when relevant
3. Include star ratings to help the customer assess product quality
4. Keep responses under 150 words
5. Suggest related products when relevant'
            ),
            JSON_OBJECT(
                'role': 'user',
                'content': 'Product reviews: ' + @context + CHAR(10) + CHAR(10) + 'Customer question: ' + @Question
            )
        ),
        'max_completion_tokens': CAST(@MaxTokens AS INT),
        'temperature': 0.5
    );

    -- Step 5: Call the model. Both @url and @credential are driven by
    -- dbo.AppConfig's AzureOpenAiEndpoint value above - deploying to a
    -- different environment is entirely a config-table change now, with
    -- nothing left in this procedure's own text to edit. The credential
    -- named by @endpoint still has to actually exist (CREATE DATABASE
    -- SCOPED CREDENTIAL [...] - a one-time setup step per environment),
    -- it's just no longer hardcoded here as well.
    EXECUTE @returnValue = sp_invoke_external_rest_endpoint
        @url = @chatUrl,
        @method = 'POST',
        @payload = @payload,
        @credential = @endpoint,
        @response = @response OUTPUT;

    -- Step 6: Extract the answer or handle errors
    IF @returnValue = 0
        SET @Answer = JSON_VALUE(@response, '$.result.choices[0].message.content');
    ELSE IF @returnValue = 429
        SET @Answer = 'The service is currently busy. Please try again in a moment.';
    ELSE IF @returnValue IN (401, 403)
        SET @Answer = 'Authentication failed. Please check the credential configuration.';
    ELSE
        SET @Answer = 'Unable to process your question at this time. HTTP status: ' + CAST(@returnValue AS NVARCHAR(10));
END;
