using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuantConnectStubsGenerator.Model;
using QuantConnectStubsGenerator.Utility;

namespace QuantConnectStubsGenerator.Parser
{
    public class PropertyParser : BaseParser
    {
        public PropertyParser(ParseContext context, SemanticModel model) : base(context, model)
        {
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            VisitProperty(node, _typeConverter.GetType(node.Type), node.Identifier.Text);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            VisitField(node, _typeConverter.GetType(node.Declaration.Type));
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            CreateEventContainerIfNecessary();

            var callableType = _typeConverter.GetType(node.Type);
            var type = new PythonType("_EventContainer")
            {
                TypeParameters = {callableType, callableType.TypeParameters.Last()}
            };

            VisitProperty(node, type, node.Identifier.Text);
        }

        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            CreateEventContainerIfNecessary();

            var callableType = _typeConverter.GetType(node.Declaration.Type);
            var type = new PythonType("_EventContainer")
            {
                TypeParameters = {callableType, callableType.TypeParameters.Last()}
            };

            VisitField(node, type);
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            var property = new Property(node.Identifier.Text)
            {
                Value = node.EqualsValue != null
                    ? FormatValue(node.EqualsValue.Value.ToString())
                    : _currentClass.Properties.Count.ToString(),
                Static = true,
                Abstract = _currentClass.Interface || HasModifier(node, "abstract"),
                DeprecationReason = GetDeprecationReason(node)
            };

            var doc = ParseDocumentation(node);
            if (doc["summary"] != null)
            {
                property.Summary = doc["summary"].GetText();
            }

            if (property.DeprecationReason != null)
            {
                property.Summary = AppendSummary(property.Summary, property.DeprecationReason);
            }

            _currentClass.Properties.Add(property);
        }

        private void VisitProperty(BasePropertyDeclarationSyntax node, PythonType type, string name)
        {
            if (HasModifier(node, "private"))
            {
                return;
            }

            if (_currentClass == null)
            {
                return;
            }

            if (_currentClass.Properties.Any(p => p.Name == name))
            {
                return;
            }

            var originalType = type;
            var typeIsEnum = false;

            if (type.Namespace != null)
            {
                var ns = _context.HasNamespace(type.Namespace)
                    ? _context.GetNamespaceByName(type.Namespace)
                    : null;

                var cls = ns?.HasClass(type) == true ? ns.GetClassByType(type) : null;

                // Python.NET converts an enum return type to an int
                if (cls?.IsEnum() == true)
                {
                    type = new PythonType("int");
                    typeIsEnum = true;
                }
            }

            // Security.Data is of type dynamic but can be used like it is of type DynamicSecurityData
            if (_currentClass.Type.ToPythonString() == "QuantConnect.Securities.Security" && name == "Data")
            {
                type = new PythonType("DynamicSecurityData", "QuantConnect.Securities");
            }

            bool readOnly = false;

            if (_typeConverter.GetSymbol(node) is IPropertySymbol)
            {
                readOnly = true;
            }

            var property = new Property(name)
            {
                Type = type,
                ReadOnly = readOnly,
                Static = _currentClass.Static || HasModifier(node, "static"),
                Abstract = _currentClass.Interface || HasModifier(node, "abstract"),
                DeprecationReason = GetDeprecationReason(node)
            };

    var doc = ParseDocumentation(node);
            if (doc["summary"] != null)
            {
                property.Summary = doc["summary"].GetText();
            }

            if (typeIsEnum)
            {
                property.Summary = AppendSummary(
                    property.Summary,
                    $"This property contains the int value of a member of the {originalType.ToPythonString()} enum.");
            }

            if (HasModifier(node, "protected"))
            {
                property.Summary = AppendSummary(property.Summary, "This property is protected.");
            }

            if (property.DeprecationReason != null)
            {
                property.Summary = AppendSummary(property.Summary, property.DeprecationReason);
            }

            _currentClass.Properties.Add(property);
        }

        private void VisitField(BaseFieldDeclarationSyntax node, PythonType type)
        {
            if (HasModifier(node, "private"))
            {
                return;
            }

            if (_currentClass == null)
            {
                return;
            }

            foreach (var variable in node.Declaration.Variables)
            {
                var property = new Property(variable.Identifier.Text)
                {
                    Type = type,
                    ReadOnly = HasModifier(node, "readonly") || HasModifier(node, "const"),
                    Static = _currentClass.Static || HasModifier(node, "static") || HasModifier(node, "const"),
                    Abstract = _currentClass.Interface || HasModifier(node, "abstract"),
                    DeprecationReason = GetDeprecationReason(node)
                };

                if (variable.Initializer != null)
                {
                    property.Value = FormatValue(variable.Initializer.Value.ToString());
                }

                var doc = ParseDocumentation(node);
                if (doc["summary"] != null)
                {
                    property.Summary = doc["summary"].GetText();
                }

                if (HasModifier(node, "protected"))
                {
                    property.Summary = AppendSummary(property.Summary, "This field is protected.");
                }

                if (property.DeprecationReason != null)
                {
                    property.Summary = AppendSummary(property.Summary, property.DeprecationReason);
                }

                _currentClass.Properties.Add(property);
            }
        }

        /// <summary>
        /// This methods generates the _EventContainer class if it doesn't exist yet.
        /// This class is used to provide accurate autocomplete on events,
        /// containing just the methods Python.NET allows to be called on event properties and fields.
        /// </summary>
        private void CreateEventContainerIfNecessary()
        {
            var classType = new PythonType("_EventContainer", _currentNamespace.Name)
            {
                TypeParameters =
                {
                    new PythonType("_EventContainer_Callable", _currentNamespace.Name)
                    {
                        IsNamedTypeParameter = true,
                    },
                    new PythonType("_EventContainer_ReturnType", _currentNamespace.Name)
                    {
                        IsNamedTypeParameter = true
                    }
                }
            };

            if (_currentNamespace.HasClass(classType))
            {
                return;
            }

            _currentNamespace.RegisterClass(new Class(classType)
            {
                Summary = "This class is used to provide accurate autocomplete on events and cannot be imported.",
                Methods =
                {
                    new Method("__iadd__", new PythonType("None"))
                    {
                        Summary = "Registers an event handler.",
                        Parameters = {new Parameter("item", classType.TypeParameters[0])}
                    },
                    new Method("__isub__", new PythonType("None"))
                    {
                        Summary = "Unregisters an event handler.",
                        Parameters = {new Parameter("item", classType.TypeParameters[0])}
                    },
                    new Method("__call__", classType.TypeParameters[1])
                    {
                        Summary = "Fires the event.",
                        Parameters =
                        {
                            new Parameter("*args", new PythonType("Any", "typing")),
                            new Parameter("**kwargs", new PythonType("Any", "typing"))
                        }
                    }
                }
            });
        }
    }
}
