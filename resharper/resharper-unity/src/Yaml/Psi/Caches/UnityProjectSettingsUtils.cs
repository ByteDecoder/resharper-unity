using System;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Modules;
using JetBrains.ReSharper.Plugins.Yaml.Psi.Tree;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.Util.Extension;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches
{
    public static class UnityProjectSettingsUtils
    {
        public static string GetUnityPathFor([NotNull] IPsiSourceFile psiSourceFile)
        {
            var solution = psiSourceFile.GetSolution();
            var solutionPath = solution.SolutionFilePath;
            var psiPath = psiSourceFile.GetLocation();
            var components = psiPath.MakeRelativeTo(solutionPath).Components.ToArray();

            var sb = new StringBuilder();
            // skip "../Assets/

            for (int i = 2; i < components.Length - 1; i++)
            {
                sb.Append(components[i]);
                sb.Append('/');
            }

            sb.Append(psiPath.NameWithoutExtension);

            return sb.ToString();
        }
        
        public static INode GetSceneCollection([NotNull] IYamlFile file)
        {
            return GetCollection(file, "EditorBuildSettings", "m_Scenes");
        }
        
        public static INode GetCollection([NotNull] IYamlFile file, string documentName, string name)
        {
            var blockMappingNode = file.Documents[0].Body.BlockNode as IBlockMappingNode;
            Assertion.Assert(blockMappingNode != null, "blockMappingNode != null");

            return GetCollection(blockMappingNode, documentName, name);
        }
        
        public static INode GetSceneCollection([NotNull] IBlockMappingNode blockMappingNode)
        {
            return GetCollection(blockMappingNode, "EditorBuildSettings", "m_Scenes");
        }

        public static INode GetCollection([NotNull] IBlockMappingNode blockMappingNode, string documentName, string name)
        {
            var documentEntry = blockMappingNode.Entries.FirstOrDefault(
                t => t.Key.GetText().Equals(documentName))?.Value as IBlockMappingNode;
            Assertion.Assert(documentEntry != null, "documentEntry != null");

            var collection = documentEntry.Entries.FirstOrDefault(t => t.Key.GetText().Equals(name))?.Value;
            Assertion.Assert(collection != null, "collection != null");
            
            return collection;
        }
        
        public static string GetUnityScenePathRepresentation(string scenePath)
        {
            return scenePath.RemoveStart("Assets/").RemoveEnd(UnityYamlFileExtensions.SceneFileExtensionWithDot);
        }
        
        public static bool IsEditorSceneManagerSceneRelatedMethod(IInvocationExpressionReference reference)
        {
            return IsSceneRelatedMethod(reference, IsEditorSceneManagerLoadScene);
        }

        public static bool IsSceneManagerSceneRelatedMethod(IInvocationExpressionReference reference)
        {
            return IsSceneRelatedMethod(reference, IsSceneManagerLoadScene);
        }

        public static ICSharpArgument GetSceneNameArgument(IInvocationExpression invocationExpression)
        {
            return invocationExpression.ArgumentList.Arguments.FirstOrDefault
                (t => t.IsNamedArgument && t.ArgumentName?.Equals("sceneName") == true || !t.IsNamedArgument);
        }

        public static string GetScenePathFromArgument(ICSharpLiteralExpression literalExpression)
        {
            // There are 3 ways to present scene name in unity
            // Consider scene : Assets/Scenes/myScene.unity
            // User could use "myScene", "Scenes/myScene" and "Assets/Scenes/myScene.unity" to load scene
            // Internally, we work only with first and second format (see UnityProjectSettingsCache)

            var constantValue = literalExpression.ConstantValue.Value as string;
            if (constantValue == null)
                return null;

            var sceneName = constantValue;
            if (sceneName.StartsWith("Assets/") && sceneName.EndsWith(UnityYamlFileExtensions.SceneFileExtensionWithDot))
                sceneName = GetUnityScenePathRepresentation(sceneName);

            return sceneName;
        }
        
        public static bool IsSceneRelatedMethod(IInvocationExpressionReference reference, Func<IMethod, bool> checker)
        {
            var result = reference.Resolve();
            if (checker(result.DeclaredElement as IMethod))
                return true;
            
            foreach (var candidate in result.Result.Candidates)
            {
                if (checker(candidate as IMethod))
                    return true;
            }

            return false;
        }

        private static bool IsSceneManagerLoadScene(IMethod method)
        {
            if (method != null && method.ShortName.StartsWith("LoadScene") &&
                method.GetContainingType()?.GetClrName().Equals(KnownTypes.SceneManager) == true)
            {
                return true;
            }

            return false;
        }
        
        private static bool IsEditorSceneManagerLoadScene(IMethod method)
        {
            if (method != null && method.ShortName.Equals("OpenScene") &&
                method.GetContainingType()?.GetClrName().Equals(KnownTypes.EditorSceneManager) == true)
            {
                return true;
            }

            return false;
        }
        
        public static UnityExternalFilesPsiModule GetUnityModule(ISolution solution)
        {
            return solution.GetComponent<UnityExternalFilesModuleFactory>().PsiModule;
        }

        public static IPsiSourceFile GetEditorBuildSettings([CanBeNull]UnityExternalFilesPsiModule psiModule)
        {
            return psiModule?.SourceFiles.FirstOrDefault(t => t.Name.Equals("EditorBuildSettings.asset"));
        }
    }
}