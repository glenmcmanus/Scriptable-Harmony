﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace NuiN.CommandConsole
{
    public class CommandConsolePresenter : MonoBehaviour
    {
        const string INVALID_PARAMETER = "invalid!";

        [SerializeField] CommandConsoleModel model;
        
        public void RegisterAssemblies()
        {
            if(model.AssemblyContainer != null) model.AssemblyContainer.FindAndRegister();
        }

        public void LoadSavedScaleAndPosition(RectTransform root)
        {
            model.ConsolePosition = root.position;
            model.ConsoleSize = root.sizeDelta;
            root.position = model.GetSavedPosition();
            root.sizeDelta = model.GetSavedSize();
        }
        
        public void RegisterCommands()
        {
            model.RegisteredCommands = new Dictionary<CommandKey, MethodInfo>();

            List<Assembly> loadedAssemblies = model.AssemblyContainer.RegisteredAssemblies.Select(Assembly.Load).ToList();
                
            foreach (var assembly in loadedAssemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                    {
                        var commandAttributes = method.GetCustomAttributes(typeof(CommandAttribute), true);
                        foreach (CommandAttribute attribute in commandAttributes)
                        {
                            if (!method.IsStatic && !typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType)) continue;

                            CommandKey commandKey = new CommandKey(attribute.command, method.GetParameters());
                            if (!model.RegisteredCommands.TryAdd(commandKey, method))
                            {
                                Debug.LogWarning($"Command already declared for [{attribute.command}] in [{method.DeclaringType}]");
                            }
                        }
                    }
                }
            }
            
            IOrderedEnumerable<KeyValuePair<CommandKey, MethodInfo>> sortedCommands = from entry in  model.RegisteredCommands orderby entry.Key.name select entry;
            model.RegisteredCommands = new Dictionary<CommandKey, MethodInfo>(sortedCommands);
        }
        
        public void InvokeCommand(TMP_InputField inputField)
        {
            string fullCommand = inputField.text;
            
            // reselect the input field and move the caret to the end for good UX
            inputField.ActivateInputField();
            inputField.caretPosition = fullCommand.Length;
            
            // no input was detected
            if (fullCommand.Trim().Length <= 0) return;
            
            // the first space after the method name indicates that parameters have been entered
            string[] commandParts = fullCommand.Split(new[] { ' ' }, 2);
            
            if (!model.RegisteredCommands.TryGetValue(model.SelectedCommand, out MethodInfo method))
            {
                Debug.Log("Command not found!");
                return;
            }
            
            ParameterInfo[] parameterInfos = method.GetParameters();
            
            List<ParameterInfo> optionalParams = parameterInfos.Where(param => param.IsOptional).ToList();
            int minParamCount = parameterInfos.Length - optionalParams.Count;
            int maxParamCount = parameterInfos.Length;

            List<object> parameters = new();
            
            // if there is no second section of the full command string, there are no parameters
            bool hasParameters = commandParts.Length > 1;
            if(hasParameters)
            {
                // separate the parameters into their own strings and remove any spaces
                string[] stringParameters = commandParts[1].Split(" ").Where(str => !string.IsNullOrEmpty(str)).ToArray();
                
                if (!HasValidParameterCount(stringParameters.Length, minParamCount, maxParamCount))
                {
                    return;
                }

                for (int i = 0; i < parameterInfos.Length; i++)
                {
                    ParameterInfo parameterInfo = parameterInfos[i];

                    // the optional parameter was not enterered so set it to the default
                    if (i >= stringParameters.Length)
                    {
                        parameters.Add(parameterInfo.DefaultValue);
                        continue;
                    }
                    
                    string stringParam = stringParameters[i];
                    
                    object param = GetParsedArg(parameterInfo.ParameterType, stringParam);
                    
                    if (param == null)
                    {
                        Debug.LogError("Invalid Parameter");
                        return;
                    }
                    
                    parameters.Add(param);
                }
            }
            
            if (!HasValidParameterCount(parameters.Count, minParamCount, maxParamCount))
            {
                return;
            }
            
            // if no parameters were entered, attempt to add any default values to the params
            if (!hasParameters || parameters.Count <= 0)
            {
                optionalParams.ForEach(param => parameters.Add(param.DefaultValue));
            }

            if (method.IsStatic)
            {
                InvokeMethod(method, null, parameters);
            }
            else
            {
                // find and invoke all instances of the method's class in the scene
                Object[] classInstances = FindObjectsByType(method.DeclaringType, FindObjectsSortMode.None);
                if (classInstances.Length <= 0)
                {
                    Debug.LogError("No instances found to run the command");
                }
                foreach (var instance in classInstances)
                {
                    InvokeMethod(method, instance, parameters);
                }
            }
            
            inputField.SetTextWithoutNotify(string.Empty);
        }

        void InvokeMethod(MethodInfo method, Object instance, List<object> parameters)
        {
            object returnValue = method.Invoke(instance, parameters.ToArray());
            if (returnValue != null)
            {
                Debug.Log(returnValue);
            }
        }
        
        bool HasValidParameterCount(int inputCount, int minCount, int maxCount)
        {
            if (inputCount < minCount)
            {
                Debug.LogError("Not enough parameters!");
                return false;
            }
            if (inputCount > maxCount)
            {
                Debug.LogError("Too many parameters!");
                return false;
            }

            return true;
        }

        public void UpdateSize(RectTransform rectTransform, Vector2 pressOffset)
        {
            // prevent scaling outside of screen
            Vector2 mousePosition = (Vector2)Input.mousePosition - pressOffset;
            mousePosition.x = Mathf.Clamp(mousePosition.x, 0, Screen.width);
            mousePosition.y = Mathf.Clamp(mousePosition.y, 0, Screen.height);
            
            if (model.InitialScalePos == Vector2.zero) model.InitialScalePos = rectTransform.position;
            if (model.InitialScale != Vector2.zero)  model.InitialScale = rectTransform.sizeDelta;

            Vector2 newSize =  (model.InitialScale + (mousePosition - model.InitialScalePos));
            newSize.x = Mathf.Clamp(newSize.x, model.MinSize.x, model.MaxScale.x);
            newSize.y = Mathf.Clamp(newSize.y, model.MinSize.y, model.MaxScale.y);
                    
            rectTransform.sizeDelta = newSize;
            model.ConsoleSize = newSize;
        }

        public void UpdatePosition(RectTransform rectTransform)
        {
            if (model.InitialMovePos == Vector2.zero) model.InitialMovePos = Input.mousePosition - rectTransform.position;
            
            Vector2 newPosition = (Vector2)Input.mousePosition - model.InitialMovePos;
            Vector2 maxPosition = new(Screen.width - rectTransform.sizeDelta.x, Screen.height - rectTransform.sizeDelta.y);
            newPosition.x = Mathf.Clamp(newPosition.x, 0, maxPosition.x);
            newPosition.y = Mathf.Clamp(newPosition.y, 0, maxPosition.y);

            rectTransform.position = newPosition;
            model.ConsolePosition = newPosition;
        }

        public void ResetInitialSizeValues()
        {
            model.InitialScalePos = Vector2.zero;
            model.InitialScale = Vector2.zero;
            
            model.SetSavedScale();
        }

        public void ResetInitialPositionValues()
        {
            model.InitialMovePos = Vector2.zero;
            
            model.SetSavedPosition();
        }

        public void ToggleConsole(GameObject console)
        {
            bool isEnabled = !model.IsConsoleEnabled;
            console.SetActive(isEnabled);
            model.IsConsoleEnabled = isEnabled;
        }

        /// <summary> Replicate CTRL+Backspace functionality on Windows </summary>
        public void DeleteTextBlock(TMP_InputField inputField)
        {
            if (!model.IsConsoleEnabled) return;
            
            string text = inputField.text;
            int caretPosition = inputField.caretPosition;

            bool willDeleteStartWord = true;
            for (int i = 0; i < inputField.caretPosition; i++)
            {
                if (inputField.text[i].ToString() == " ") willDeleteStartWord = false;
            }
            
            if (caretPosition > 0 && caretPosition <= text.Length)
            {
                int startIndex = caretPosition - 1;
                while (startIndex > 0 && !char.IsWhiteSpace(text[startIndex - 1])) startIndex--;

                string newText = text.Remove(startIndex, caretPosition - startIndex);

                inputField.text = newText;
                inputField.caretPosition = startIndex;
            }

            if (willDeleteStartWord) StartCoroutine(SetCaretPosition(inputField, 0));
            else inputField.text += " ";
        }
        
        /// <summary> Hack to properly set caret position </summary>
        static IEnumerator SetCaretPosition(TMP_InputField inputField, int index)
        {
            int width = inputField.caretWidth;
            inputField.caretWidth = 0;

            yield return new WaitForEndOfFrame();
            
            inputField.caretWidth = width;
            inputField.caretPosition = index;
        }

        public void AutoCompleteAndSetCommand(string inputText)
        {
            foreach (KeyValuePair<CommandKey, MethodInfo> command in model.RegisteredCommands)
            {
                string commandName = command.Key.name;
                if (inputText.Length <= 0 || commandName.ToLower().StartsWith(inputText.ToLower()))
                {
                    MethodInfo methodInfo = command.Value;
                    
                    model.SelectedCommand = command.Key;

                    string parameters = string.Empty;
                    foreach (var param in methodInfo.GetParameters())
                    {
                        parameters += $" {GetTypeName(param.ParameterType)}";
                    }
                    Debug.Log("new command:" + commandName + parameters);
                }
            }
        }

        static string GetTypeName(Type type)
        {
            return type switch
            {
                not null when type == typeof(float) => "float",
                not null when type == typeof(bool) => "bool",
                not null when type == typeof(int) => "int",
                not null when type == typeof(string) => "string",
                not null when type == typeof(Vector3) => "Vector3",
                not null when type == typeof(Vector2) => "Vector2",
                not null when type == typeof(Vector2Int) => "Vector2Int",
                not null when type == typeof(Vector3Int) => "Vector3Int",
                not null when type == typeof(long) => "long",
                not null when type == typeof(ulong) => "ulong",
                not null when type == typeof(double) => "double",
                not null when type == typeof(byte) => "byte",
                not null when type == typeof(sbyte) => "sbyte",
                not null when type == typeof(short) => "short",
                not null when type == typeof(char) => "char",
                _ => INVALID_PARAMETER
            };
        }
        
        static object GetParsedArg(Type type, string arg)
        {
            if (type == typeof(string)) return arg;
            if(type == typeof(float) && float.TryParse(arg, out float floatVal)) return floatVal;
            if(type == typeof(int) && int.TryParse(arg, out int intVal)) return intVal;
            if(type == typeof(bool) && bool.TryParse(arg, out bool boolVal)) return boolVal;
            
            // split the arg by commas. example valid vector3 arg: "3.2,1.5"
            string[] commaSplitValues = arg.Split(",");
            if(type == typeof(Vector2) && commaSplitValues.Length == 2)
            {
                if (float.TryParse(commaSplitValues[0], out float f1) && float.TryParse(commaSplitValues[1], out float f2))
                    return new Vector2(f1, f2);
            }
            if(type == typeof(Vector3) && commaSplitValues.Length == 3)
            {
                if (float.TryParse(commaSplitValues[0], out float f1) && float.TryParse(commaSplitValues[1], out float f2) && float.TryParse(commaSplitValues[2], out float f3))
                    return new Vector3(f1, f2, f3);
            }
            if(type == typeof(Vector2Int) && commaSplitValues.Length == 2)
            {
                if (int.TryParse(commaSplitValues[0], out int i1) && int.TryParse(commaSplitValues[1], out int i2))
                    return new Vector2Int(i1, i2);
            }
            if(type == typeof(Vector3Int) && commaSplitValues.Length == 3)
            {
                if (int.TryParse(commaSplitValues[0], out int i1) && int.TryParse(commaSplitValues[1], out int i2) && int.TryParse(commaSplitValues[2], out int i3))
                    return new Vector3Int(i1, i2, i3);
            }
            
            if(type == typeof(long) && long.TryParse(arg, out long longVal)) return longVal;
            if(type == typeof(ulong) && ulong.TryParse(arg, out ulong ulongVal)) return ulongVal;
            if(type == typeof(double) && double.TryParse(arg, out double doubleVal)) return doubleVal;
            if(type == typeof(byte) && byte.TryParse(arg, out byte byteVal)) return byteVal;
            if(type == typeof(sbyte) && sbyte.TryParse(arg, out sbyte sbyteVal)) return sbyteVal;
            if(type == typeof(short) && short.TryParse(arg, out short shortVal)) return shortVal;

            return null;
        }
    }
}