using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UdonSharp;

namespace Kurotori
{
    /// <summary>
    /// UdonSharpのC#スクリプトファイルを自動的に検出し、対応するUdonSharpProgramAssetを生成するAssetPostprocessor
    /// </summary>
    public class UdonSharpAutoAssetGenerator : AssetPostprocessor
    {
        /// <summary>
        /// インポートされたアセットを処理
        /// </summary>
        /// <param name="importedAssets">インポートされたアセットのパス配列</param>
        /// <param name="deletedAssets">削除されたアセットのパス配列</param>
        /// <param name="movedAssets">移動されたアセットのパス配列</param>
        /// <param name="movedFromAssetPaths">移動前のアセットのパス配列</param>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // インポートされたアセットの中からC#スクリプトファイルを検索
            foreach (string assetPath in importedAssets)
            {
                if (Path.GetExtension(assetPath).ToLower() == ".cs")
                {
                    ProcessCSharpScript(assetPath);
                }
            }

            // 移動されたアセットも処理（リネームも含む）
            for (int i = 0; i < movedAssets.Length; i++)
            {
                string movedAssetPath = movedAssets[i];
                string oldAssetPath = movedFromAssetPaths[i];

                if (Path.GetExtension(movedAssetPath).ToLower() == ".cs")
                {
                    ProcessMovedCSharpScript(movedAssetPath, oldAssetPath);
                }
            }
        }

        /// <summary>
        /// C#スクリプトファイルを処理し、UdonSharpBehaviourを継承している場合はUdonSharpProgramAssetを生成
        /// </summary>
        /// <param name="scriptPath">C#スクリプトファイルのパス</param>
        private static void ProcessCSharpScript(string scriptPath)
        {
            try
            {
                // MonoScriptとしてロード
                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (monoScript == null)
                {
                    return;
                }

                // スクリプトのクラス型を取得
                System.Type scriptType = monoScript.GetClass();
                if (scriptType == null)
                {
                    // コンパイルエラーなどで型が取得できない場合はスキップ
                    return;
                }

                // UdonSharpBehaviourを継承しているかチェック
                if (!IsUdonSharpBehaviour(scriptType))
                {
                    return;
                }

                // 既に対応するUdonSharpProgramAssetが存在するかチェック
                if (HasExistingProgramAsset(monoScript))
                {
                    Debug.Log($"UdonSharpProgramAsset already exists for: {scriptPath}");
                    return;
                }

                // UdonSharpProgramAssetを生成
                CreateUdonSharpProgramAsset(monoScript, scriptPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing UdonSharp script '{scriptPath}': {e.Message}");
            }
        }

        /// <summary>
        /// 移動されたC#スクリプトファイルを処理し、対応するUdonSharpProgramAssetも移動またはリネーム
        /// </summary>
        /// <param name="newScriptPath">新しいC#スクリプトファイルのパス</param>
        /// <param name="oldScriptPath">古いC#スクリプトファイルのパス</param>
        private static void ProcessMovedCSharpScript(string newScriptPath, string oldScriptPath)
        {
            try
            {
                // 新しいスクリプトを処理（必要に応じてProgramAssetを生成）
                ProcessCSharpScript(newScriptPath);

                // 古いProgramAssetが存在する場合は新しい名前に合わせて移動/リネーム
                string oldAssetPath = GetExpectedProgramAssetPath(oldScriptPath);
                string newAssetPath = GetExpectedProgramAssetPath(newScriptPath);

                if (File.Exists(oldAssetPath) && oldAssetPath != newAssetPath)
                {
                    string result = AssetDatabase.MoveAsset(oldAssetPath, newAssetPath);
                    if (string.IsNullOrEmpty(result))
                    {
                        Debug.Log($"Moved UdonSharpProgramAsset: {oldAssetPath} -> {newAssetPath}");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to move UdonSharpProgramAsset: {result}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing moved UdonSharp script '{newScriptPath}': {e.Message}");
            }
        }

        /// <summary>
        /// 指定された型がUdonSharpBehaviourを継承しているかチェック
        /// </summary>
        /// <param name="type">チェックする型</param>
        /// <returns>UdonSharpBehaviourを継承している場合はtrue</returns>
        private static bool IsUdonSharpBehaviour(System.Type type)
        {
            if (type == null) return false;

            System.Type udonSharpBehaviourType = typeof(UdonSharpBehaviour);
            return udonSharpBehaviourType.IsAssignableFrom(type);
        }

        /// <summary>
        /// 指定されたMonoScriptに対応するUdonSharpProgramAssetが既に存在するかチェック
        /// </summary>
        /// <param name="monoScript">チェックするMonoScript</param>
        /// <returns>対応するUdonSharpProgramAssetが存在する場合はtrue</returns>
        private static bool HasExistingProgramAsset(MonoScript monoScript)
        {
            // すべてのUdonSharpProgramAssetを検索
            string[] programAssetGuids = AssetDatabase.FindAssets("t:UdonSharpProgramAsset", new[] { "Assets" });

            foreach (string guid in programAssetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                UdonSharpProgramAsset programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);

                if (programAsset != null)
                {
                    MonoScript sourceScript = GetSourceCsScript(programAsset);
                    if (sourceScript == monoScript)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// UdonSharpProgramAssetのsourceCsScriptフィールドを取得
        /// </summary>
        /// <param name="programAsset">UdonSharpProgramAsset</param>
        /// <returns>sourceCsScriptフィールドの値</returns>
        private static MonoScript GetSourceCsScript(UdonSharpProgramAsset programAsset)
        {
            try
            {
                // SerializedObjectを使ってsourceCsScriptフィールドにアクセス
                SerializedObject serializedObject = new SerializedObject(programAsset);
                SerializedProperty sourceCsScriptProperty = serializedObject.FindProperty("sourceCsScript");

                if (sourceCsScriptProperty != null && sourceCsScriptProperty.objectReferenceValue != null)
                {
                    return sourceCsScriptProperty.objectReferenceValue as MonoScript;
                }

                // リフレクションを使ってsourceCsScriptフィールドにアクセス
                var sourceCsScriptField = typeof(UdonSharpProgramAsset).GetField("sourceCsScript",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                if (sourceCsScriptField != null)
                {
                    return sourceCsScriptField.GetValue(programAsset) as MonoScript;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to get sourceCsScript from UdonSharpProgramAsset: {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// UdonSharpProgramAssetを生成
        /// </summary>
        /// <param name="monoScript">対応するMonoScript</param>
        /// <param name="scriptPath">C#スクリプトファイルのパス</param>
        private static void CreateUdonSharpProgramAsset(MonoScript monoScript, string scriptPath)
        {
            try
            {
                string assetPath = GetExpectedProgramAssetPath(scriptPath);

                // 既に同名のアセットが存在する場合は確認
                if (File.Exists(assetPath))
                {
                    Debug.LogWarning($"UdonSharpProgramAsset already exists at path: {assetPath}");
                    return;
                }

                // UdonSharpProgramAssetを作成
                UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();

                // sourceCsScriptフィールドを設定
                SetSourceCsScript(programAsset, monoScript);

                // アセットとして保存
                AssetDatabase.CreateAsset(programAsset, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"Created UdonSharpProgramAsset: {assetPath} for script: {scriptPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to create UdonSharpProgramAsset for '{scriptPath}': {e.Message}");
            }
        }

        /// <summary>
        /// UdonSharpProgramAssetのsourceCsScriptフィールドを設定
        /// </summary>
        /// <param name="programAsset">UdonSharpProgramAsset</param>
        /// <param name="monoScript">設定するMonoScript</param>
        private static void SetSourceCsScript(UdonSharpProgramAsset programAsset, MonoScript monoScript)
        {
            try
            {
                // SerializedObjectを使ってsourceCsScriptフィールドを設定
                SerializedObject serializedObject = new SerializedObject(programAsset);
                SerializedProperty sourceCsScriptProperty = serializedObject.FindProperty("sourceCsScript");

                if (sourceCsScriptProperty != null)
                {
                    sourceCsScriptProperty.objectReferenceValue = monoScript;
                    serializedObject.ApplyModifiedProperties();
                    return;
                }

                // リフレクションを使ってsourceCsScriptフィールドを設定
                var sourceCsScriptField = typeof(UdonSharpProgramAsset).GetField("sourceCsScript",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                if (sourceCsScriptField != null)
                {
                    sourceCsScriptField.SetValue(programAsset, monoScript);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to set sourceCsScript on UdonSharpProgramAsset: {e.Message}");
            }
        }

        /// <summary>
        /// C#スクリプトファイルのパスから期待されるUdonSharpProgramAssetのパスを生成
        /// </summary>
        /// <param name="scriptPath">C#スクリプトファイルのパス</param>
        /// <returns>UdonSharpProgramAssetのパス</returns>
        private static string GetExpectedProgramAssetPath(string scriptPath)
        {
            string directory = Path.GetDirectoryName(scriptPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(scriptPath);
            return Path.Combine(directory, fileNameWithoutExtension + ".asset").Replace('\\', '/');
        }

        /// <summary>
        /// ファイル名を無害化（CreateUdonSharpFile.csのSanitizeNameメソッドを参考）
        /// </summary>
        /// <param name="name">無害化する名前</param>
        /// <returns>無害化された名前</returns>
        private static string SanitizeName(string name)
        {
            return name.Replace(" ", "")
                        .Replace("#", "Sharp")
                        .Replace("(", "")
                        .Replace(")", "")
                        .Replace("*", "")
                        .Replace("<", "")
                        .Replace(">", "")
                        .Replace("-", "_")
                        .Replace("!", "")
                        .Replace("$", "")
                        .Replace("@", "")
                        .Replace("+", "");
        }
    }
}