//
// NonPublicMethodWithTestAttributeAnalyzer.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2013 Xamarin Inc. (http://xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using System.Threading;
using ICSharpCode.NRefactory6.CSharp.Refactoring;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.FindSymbols;

namespace ICSharpCode.NRefactory6.CSharp.Diagnostics
{
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	[NRefactoryCodeDiagnosticAnalyzer(AnalysisDisableKeyword = "NUnit.NonPublicMethodWithTestAttribute")]
	public class NonPublicMethodWithTestAttributeAnalyzer : GatherVisitorDiagnosticAnalyzer
	{
		internal const string DiagnosticId  = "NonPublicMethodWithTestAttributeAnalyzer.;
		const string Description            = "Non public methods are not found by NUnit";
		const string MessageFormat          = "NUnit test methods should be public";
		const string Category               = DiagnosticAnalyzerCategories.NUnit;

		static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor (DiagnosticId, Description, MessageFormat, Category, DiagnosticSeverity.Info, true, "NUnit Test methods should have public visibility");

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics {
			get {
				return ImmutableArray.Create(Rule);
			}
		}

		protected override CSharpSyntaxWalker CreateVisitor (SemanticModel semanticModel, Action<Diagnostic> addDiagnostic, CancellationToken cancellationToken)
		{
			return new GatherVisitor(semanticModel, addDiagnostic, cancellationToken);
		}

		class GatherVisitor : GatherVisitorBase<NonPublicMethodWithTestAttributeAnalyzer>
		{
			public GatherVisitor(SemanticModel semanticModel, Action<Diagnostic> addDiagnostic, CancellationToken cancellationToken)
				: base (semanticModel, addDiagnostic, cancellationToken)
			{
			}

			public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
			{
				//missing this, we don't visit trivia - so the resharper disable is ignored
				base.VisitMethodDeclaration(node);
				IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(node);
				if (methodSymbol == null || methodSymbol.IsOverride || methodSymbol.IsStatic || node.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
					return;

				if (!methodSymbol.GetAttributes().Any(a => a.AttributeClass.Name == "TestAttribute" && a.AttributeClass.ContainingNamespace.ToDisplayString() == "NUnit.Framework"))
					return;

				AddDiagnosticAnalyzer(Diagnostic.Create(Rule, node.Identifier.GetLocation()));
			}

			public override void VisitBlock(BlockSyntax node)
			{
			}
		}
	}

	[ExportCodeFixProvider(NonPublicMethodWithTestAttributeAnalyzer.DiagnosticId, LanguageNames.CSharp)]
	public class NonPublicMethodWithTestAttributeFixProvider : NRefactoryCodeFixProvider
	{
		protected override IEnumerable<string> InternalGetFixableDiagnosticIds()
		{
			yield return NonPublicMethodWithTestAttributeAnalyzer.DiagnosticId;
		}

		public override FixAllProvider GetFixAllProvider()
		{
			return WellKnownFixAllProviders.BatchFixer;
		}

		public async override Task RegisterCodeFixesAsync(CodeFixContext context)
		{
			var document = context.Document;
			var cancellationToken = context.CancellationToken;
			var span = context.Span;
			var diagnostics = context.Diagnostics;
			var root = await document.GetSyntaxRootAsync(cancellationToken);
			var result = new List<CodeAction>();
			foreach (var diagnostic in diagnostics) {
				var node = root.FindNode(diagnostic.Location.SourceSpan) as MethodDeclarationSyntax;
				if (node == null)
					continue;

				Func<SyntaxToken, bool> isModifierToRemove =
					m => (m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

				// Get trivia for new modifier
				var leadingTrivia = SyntaxTriviaList.Empty;
				var trailingTrivia = SyntaxTriviaList.Create(SyntaxFactory.Space);
				var removedModifiers = node.Modifiers.Where(isModifierToRemove);
				if (removedModifiers.Any())
				{
					leadingTrivia = removedModifiers.First().LeadingTrivia;
				}
				else
				{
					// Method begins directly with return type, use its leading trivia
					leadingTrivia = node.ReturnType.GetLeadingTrivia();
				}

				var newMethod = node.WithModifiers(SyntaxFactory.TokenList(new SyntaxTokenList()
					.Add(SyntaxFactory.Token(leadingTrivia, SyntaxKind.PublicKeyword, trailingTrivia))
					.AddRange(node.Modifiers.ToArray().Where(m => !isModifierToRemove(m)))))
					.WithReturnType(node.ReturnType.WithoutLeadingTrivia());
                var newRoot = root.ReplaceNode((SyntaxNode)node, newMethod);
				context.RegisterCodeFix(CodeActionFactory.Create(node.Span, diagnostic.Severity, "Make method public", document.WithSyntaxRoot(newRoot)), diagnostic);
			}
		}
	}
}