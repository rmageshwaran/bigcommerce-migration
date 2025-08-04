using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace BigCommerce.Migration.CodeAnalysis
{

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DurableFunctionAnalyzer : DiagnosticAnalyzer
{
    // DateTime violations
    public static readonly DiagnosticDescriptor DateTimeNowRule = new DiagnosticDescriptor(
        "BM0001",
        "Do not use DateTime.Now or DateTime.UtcNow in orchestrator functions",
        "DateTime.{0} is non-deterministic. Use context.CurrentUtcDateTime instead",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Use context.CurrentUtcDateTime instead of DateTime.Now or DateTime.UtcNow.");

    // GUID violations
    public static readonly DiagnosticDescriptor GuidNewGuidRule = new DiagnosticDescriptor(
        "BM0002",
        "Do not use Guid.NewGuid() in orchestrator functions",
        "Guid.NewGuid() is non-deterministic. Use context.NewGuid() instead",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Use context.NewGuid() instead of Guid.NewGuid().");

    // Task.Delay violations
    public static readonly DiagnosticDescriptor TaskDelayRule = new DiagnosticDescriptor(
        "BM0003",
        "Do not use Task.Delay or Thread.Sleep in orchestrator functions",
        "{0} is non-deterministic. Use context.CreateTimer() instead",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Use context.CreateTimer() instead of Task.Delay or Thread.Sleep.");

    // Random violations
    public static readonly DiagnosticDescriptor RandomRule = new DiagnosticDescriptor(
        "BM0004",
        "Do not use Random in orchestrator functions",
        "Random number generation is non-deterministic. Move to activity function",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Move random number generation to activity functions.");

    // HTTP calls violations
    public static readonly DiagnosticDescriptor HttpCallRule = new DiagnosticDescriptor(
        "BM0005",
        "Do not make HTTP calls in orchestrator functions",
        "HTTP calls are non-deterministic. Move to activity function",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Move HTTP calls to activity functions.");

    // ConfigureAwait(false) violations
    public static readonly DiagnosticDescriptor ConfigureAwaitRule = new DiagnosticDescriptor(
        "BM0006",
        "Do not use ConfigureAwait(false) in orchestrator functions",
        "ConfigureAwait(false) can cause non-deterministic behavior",
        "Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Orchestrator functions must be deterministic. Remove ConfigureAwait(false) calls.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DateTimeNowRule, GuidNewGuidRule, TaskDelayRule, RandomRule, HttpCallRule, ConfigureAwaitRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccessExpression, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        
        // Check if we're in an orchestrator function
        if (!IsInOrchestratorFunction(invocation))
            return;

        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null)
            return;

        var memberName = memberAccess.Name.Identifier.ValueText;

        // Check for DateTime.Now or DateTime.UtcNow
        if (memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "DateTime")
        {
            if (memberName == "Now" || memberName == "UtcNow")
            {
                var diagnostic = Diagnostic.Create(DateTimeNowRule, invocation.GetLocation(), memberName);
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for Guid.NewGuid()
        if (memberAccess.Expression is IdentifierNameSyntax guidIdentifier && guidIdentifier.Identifier.ValueText == "Guid")
        {
            if (memberName == "NewGuid")
            {
                var diagnostic = Diagnostic.Create(GuidNewGuidRule, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for Task.Delay
        if (memberAccess.Expression is IdentifierNameSyntax taskIdentifier && taskIdentifier.Identifier.ValueText == "Task")
        {
            if (memberName == "Delay")
            {
                var diagnostic = Diagnostic.Create(TaskDelayRule, invocation.GetLocation(), "Task.Delay");
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for Thread.Sleep
        if (memberAccess.Expression is IdentifierNameSyntax threadIdentifier && threadIdentifier.Identifier.ValueText == "Thread")
        {
            if (memberName == "Sleep")
            {
                var diagnostic = Diagnostic.Create(TaskDelayRule, invocation.GetLocation(), "Thread.Sleep");
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for new Random()
        if (invocation.Expression is ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.Type is IdentifierNameSyntax randomIdentifier && randomIdentifier.Identifier.ValueText == "Random")
            {
                var diagnostic = Diagnostic.Create(RandomRule, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for HttpClient usage
        if (memberAccess.Expression is IdentifierNameSyntax httpIdentifier && httpIdentifier.Identifier.ValueText == "HttpClient")
        {
            var diagnostic = Diagnostic.Create(HttpCallRule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        // Check for ConfigureAwait(false)
        if (memberName == "ConfigureAwait" && invocation.ArgumentList.Arguments.Count == 1)
        {
            var argument = invocation.ArgumentList.Arguments[0];
            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == "false")
            {
                var diagnostic = Diagnostic.Create(ConfigureAwaitRule, invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzeMemberAccessExpression(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        
        // Check if we're in an orchestrator function
        if (!IsInOrchestratorFunction(memberAccess))
            return;

        // Check for DateTime.Now or DateTime.UtcNow property access
        if (memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "DateTime")
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName == "Now" || memberName == "UtcNow")
            {
                var diagnostic = Diagnostic.Create(DateTimeNowRule, memberAccess.GetLocation(), memberName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsInOrchestratorFunction(SyntaxNode node)
    {
        // Check if the node is within a method that has OrchestrationTrigger attribute
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method == null)
            return false;

        // Check for OrchestrationTrigger attribute
        var hasOrchestrationTrigger = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString().Contains("OrchestrationTrigger"));

        if (hasOrchestrationTrigger)
            return true;

        // Check if the file is a Durable Functions orchestrator (more specific patterns)
        var fileName = node.SyntaxTree.FilePath;
        if (fileName != null)
        {
            // Only target files that are actual Durable Functions orchestrators
            // These should be in specific directories and have specific naming patterns
            var isInOrchestratorDirectory = fileName.Contains("/Orchestrators/") || fileName.Contains("\\Orchestrators\\");
            var isDurableOrchestrator = fileName.Contains("DurableOrchestrator") || 
                                      fileName.Contains("MigrationOrchestrator") || 
                                      fileName.Contains("EntityMigrationOrchestrator");
            
            return isInOrchestratorDirectory && isDurableOrchestrator;
        }

        return false;
    }
}
} 