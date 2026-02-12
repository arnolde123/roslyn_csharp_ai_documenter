using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                Console.WriteLine("Make sure to set AzureOpenAI:Endpoint, AzureOpenAI:ApiKey, and AzureOpenAI:DeploymentName");
                return;
            }

            // Load source code from file or use default example
            string sourceCode;
            if (File.Exists(inputFile))
            {
                Console.WriteLine($"Reading code from {inputFile}...");
                sourceCode = await File.ReadAllTextAsync(inputFile);
            }
            else
            {
                Console.WriteLine($"File {inputFile} not found. Using example code...");
                sourceCode = @"
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

            // 4. Generate Documentation in Parallel
            var openAiClient = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            var docMap = new ConcurrentDictionary<SyntaxNode, string>();

            var tasks = collector.Targets.Select(async node =>
            {
                try
                {
                    // Extract just the signature and body for the AI context
                    string snippet = node.ToString();
                    
                    // Use Semantic Model to get the full symbol name (helpful for the AI to know context)
                    var symbol = semanticModel.GetDeclaredSymbol(node);
                    string symbolName = symbol?.ToDisplayString() ?? "Unknown";

                    string generatedDoc = await GenerateDocWithOpenAi(openAiClient, deploymentName, symbolName, snippet);
                    docMap.TryAdd(node, generatedDoc);
                    Console.WriteLine($"✓ Generated docs for: {symbolName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Error generating docs for {node}: {ex.Message}");
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
                Console.WriteLine($"AI Error for {contextName}: {ex.Message}");
                return $"/// <summary>\n/// Documentation generation failed: {ex.Message}\n/// </summary>\n";
            }
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
    /// </summary>
    class DocRewriter : CSharpSyntaxRewriter
    {
        private readonly IDictionary<SyntaxNode, string> _docs;

        public DocRewriter(IDictionary<SyntaxNode, string> docs)
        {
            _docs = docs;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            // If this node is in our map, add the docs
            if (node != null && _docs.TryGetValue(node, out var docXml))
            {
                // Get existing leading trivia (indentation, etc.)
                var existingTrivia = node.GetLeadingTrivia();
                
                // Find the indentation from existing trivia
                var indentation = existingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
                
                // If no indentation found, try to infer from parent or use default
                if (indentation == default(SyntaxTrivia))
                {
                    // Default to 4 spaces if we can't find existing indentation
                    indentation = SyntaxFactory.Whitespace("    ");
                }
                
                // Parse the XML documentation into trivia
                var docTrivia = SyntaxFactory.ParseLeadingTrivia(docXml);
                
                // Combine: documentation + indentation + any other existing trivia
                var newTrivia = docTrivia.Add(indentation);

                // Update the node with new leading trivia
                return base.Visit(node.WithLeadingTrivia(newTrivia));
            }

            return base.Visit(node);
        }
    }
}