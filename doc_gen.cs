using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading; // Added for SemaphoreSlim
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Configuration;

namespace AiDocGenerator
{
    class Program
    {
        private static IConfiguration? _configuration;

        static async Task Main(string[] args)
        {
            // Load configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();

            var endpoint = _configuration["AzureOpenAI:Endpoint"];
            var apiKey = _configuration["AzureOpenAI:ApiKey"];
            var deploymentName = _configuration["AzureOpenAI:DeploymentName"];
            var inputFile = _configuration["InputFile"] ?? "input.cs";
            var outputFile = _configuration["OutputFile"] ?? "output.cs";

            // Validate configuration
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_KEY_HERE")
            {
                Console.WriteLine("ERROR: Please configure your Azure OpenAI settings in appsettings.json");
                return;
            }

            // Load source code
            string sourceCode;
            if (File.Exists(inputFile))
            {
                Console.WriteLine($"Reading code from {inputFile}...");
                sourceCode = await File.ReadAllTextAsync(inputFile);
            }
            else
            {
                Console.WriteLine($"File {inputFile} not found. Using example code...");
                sourceCode = GetExampleCode();
            }

            Console.WriteLine("\n--- Analyzing Code ---");
            var result = await AddDocumentationAsync(sourceCode, endpoint, apiKey, deploymentName);
            
            // Write output to file
            await File.WriteAllTextAsync(outputFile, result);
            Console.WriteLine($"\n--- Generated Code written to {outputFile} ---\n");
            Console.WriteLine(result);
        }

        public static async Task<string> AddDocumentationAsync(string sourceCode, string endpoint, string apiKey, string deploymentName)
        {
            // 1. Create Syntax Tree
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = await syntaxTree.GetRootAsync();

            // 2. Create Compilation (Semantic Model)
            var compilation = CSharpCompilation.Create("DocGen")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);

            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            // 3. Find Undocumented Nodes using a Walker
            var collector = new UndocumentedNodeCollector();
            collector.Visit(root);

            if (collector.Targets.Count == 0)
            {
                Console.WriteLine("No missing documentation found.");
                return sourceCode;
            }

            Console.WriteLine($"Found {collector.Targets.Count} undocumented items. Generating docs...");

            // 4. Generate Documentation with Throttling (Avoids Rate Limits)
            var openAiClient = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            
            // Key = Node.SpanStart (Int position), Value = Generated XML
            // We use SpanStart because SyntaxNode references change during rewriting.
            var docMap = new Dictionary<int, string>(); 
            
            int maxConcurrent = 3; // Limit to 3 concurrent requests to avoid 429 errors
            var semaphore = new SemaphoreSlim(maxConcurrent);
            
            var tasks = collector.Targets.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Extract just the signature and body for the AI context
                    string snippet = node.ToString();
                    
                    // Use Semantic Model to get the full symbol name
                    var symbol = semanticModel.GetDeclaredSymbol(node);
                    string symbolName = symbol?.ToDisplayString() ?? "Unknown";

                    string generatedDoc = await GenerateDocWithOpenAi(openAiClient, deploymentName, symbolName, snippet);
                    
                    lock (docMap)
                    {
                        docMap[node.SpanStart] = generatedDoc;
                    }
                    Console.WriteLine($"✓ Generated docs for: {symbolName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Error generating docs for {node.SpanStart}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // 5. Rewrite the Syntax Tree
            var rewriter = new DocRewriter(docMap);
            var newRoot = rewriter.Visit(root);

            return newRoot.ToFullString();
        }

        private static async Task<string> GenerateDocWithOpenAi(OpenAIClient client, string deploymentName, string contextName, string codeSnippet)
        {
            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage("You are a C# documentation expert. Output ONLY the XML documentation comments (/// <summary>...). Do not output Markdown blocks. Do not output the function code again. Include <param> tags for all parameters and <returns> tags for methods that return values."),
                    new ChatRequestUserMessage($"Generate XML summary and param tags for this symbol: {contextName}\n\nCode:\n{codeSnippet}")
                },
                Temperature = (float)0.2, 
            };

            try
            {
                Response<ChatCompletions> response = await client.GetChatCompletionsAsync(chatCompletionsOptions);
                string doc = response.Value.Choices[0].Message.Content.Trim();
                
                // Cleanup: Ensure it ends with a newline
                if (!doc.EndsWith(Environment.NewLine)) doc += Environment.NewLine;
                
                return doc;
            }
            catch (Exception ex)
            {
                // Simple retry or error reporting logic could go here
                throw; 
            }
        }

        private static string GetExampleCode()
        {
            return @"
using System;
namespace DemoApp
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        /// <summary>
        /// Existing documentation should be ignored.
        /// </summary>
        public void PrintResult(int result)
        {
            Console.WriteLine(result);
        }

        public double Divide(double a, double b) => a / b;
    }
}";
        }
    }

    // --- ROSLYN HELPERS ---

    /// <summary>
    /// Walks the tree to find methods and classes without XML documentation.
    /// </summary>
    class UndocumentedNodeCollector : CSharpSyntaxWalker
    {
        public List<SyntaxNode> Targets { get; } = new List<SyntaxNode>();

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!HasDocumentation(node)) Targets.Add(node);
            base.VisitClassDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (!HasDocumentation(node)) Targets.Add(node);
            base.VisitMethodDeclaration(node);
        }

        private bool HasDocumentation(SyntaxNode node)
        {
            var trivia = node.GetLeadingTrivia();
            return trivia.Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) 
                                || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
        }
    }

    /// <summary>
    /// Rewrites the tree by inserting the generated documentation.
    /// Uses Position-Based matching and Bottom-Up rewriting.
    /// </summary>
    class DocRewriter : CSharpSyntaxRewriter
    {
        private readonly IDictionary<int, string> _docs;

        public DocRewriter(IDictionary<int, string> docs)
        {
            _docs = docs;
        }

        private SyntaxNode AddDocumentation(SyntaxNode node, string docXml)
        {
            // Get existing leading trivia (indentation, comments, etc.)
            var existingTrivia = node.GetLeadingTrivia();
            
            // Find the last whitespace trivia to determine indentation
            var whitespace = existingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            var indentText = whitespace.ToString();
            
            if (string.IsNullOrEmpty(indentText))
            {
                indentText = "    "; // Default 4 spaces
            }

            // Split the generated XML into lines so we can indent each line correctly
            var docLines = docXml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            // Reassemble with correct indentation
            var indentedDocBuilder = new StringBuilder();
            foreach (var line in docLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    indentedDocBuilder.AppendLine(indentText + line.TrimStart());
                }
            }
            
            // Parse the correctly indented XML into Trivia
            var docTrivia = SyntaxFactory.ParseLeadingTrivia(indentedDocBuilder.ToString());
            
            // Combine: Existing Trivia + New Docs
            // We append the new docs to the end of the existing trivia list
            // (e.g. after any #region directives but before the node itself)
            var newTrivia = existingTrivia.AddRange(docTrivia);

            return node.WithLeadingTrivia(newTrivia);
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // 1. Visit children (Methods) FIRST.
            // This ensures we rewrite the inner methods before processing the class itself.
            var rewrittenNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // 2. Check if THIS class (using original SpanStart) has documentation to add.
            if (_docs.TryGetValue(node.SpanStart, out var docXml))
            {
                return AddDocumentation(rewrittenNode, docXml);
            }

            return rewrittenNode;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // 1. Visit children FIRST.
            var rewrittenNode = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

            // 2. Check if THIS method (using original SpanStart) has documentation to add.
            if (_docs.TryGetValue(node.SpanStart, out var docXml))
            {
                return AddDocumentation(rewrittenNode, docXml);
            }

            return rewrittenNode;
        }
    }
}