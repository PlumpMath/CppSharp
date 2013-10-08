﻿using System.Collections.Generic;
using System.Linq;
using CppSharp.AST;
using CppSharp.Generators.CSharp;

namespace CppSharp.Passes
{
    public class MultipleInheritancePass : TranslationUnitPass
    {
        /// <summary>
        /// Collects all interfaces in a unit to be added at the end 
        /// because the unit cannot be changed while it's being iterated though.
        /// We also need it to check if a class already has a complementary interface
        /// because different classes may have the same secondary bases.
        /// </summary>
        private readonly Dictionary<Class, Class> interfaces = new Dictionary<Class, Class>();

        public override bool VisitTranslationUnit(TranslationUnit unit)
        {
            bool result = base.VisitTranslationUnit(unit);
            foreach (var @interface in interfaces)
                @interface.Key.Namespace.Classes.Add(@interface.Value);
            interfaces.Clear();
            return result;
        }

        public override bool VisitClassDecl(Class @class)
        {
            // skip the first base because we can inherit from one class
            for (int i = 1; i < @class.Bases.Count; i++)
            {
                var @base = @class.Bases[i].Class;
                if (@base.IsInterface) continue;

                var @interface = GetInterface(@class, @base, true);
                @class.Bases[i] = new BaseClassSpecifier { Type = new TagType(@interface) };
            }
            return base.VisitClassDecl(@class);
        }

        private Class GetInterface(Class @class, Class @base, bool addMembers = false)
        {
            if (@base.CompleteDeclaration != null)
                @base = (Class) @base.CompleteDeclaration;
            var name = "I" + @base.Name;
            if (interfaces.ContainsKey(@base))
                return interfaces[@base];

            return @base.Namespace.Classes.FirstOrDefault(c => c.Name == name) ??
                GetNewInterface(@class, name, @base, addMembers);
        }

        private Class GetNewInterface(Class @class, string name, Class @base, bool addMembers = false)
        {
            var @interface = new Class
                {
                    Name = name,
                    Namespace = @base.Namespace,
                    Access = @base.Access,
                    Type = ClassType.Interface,
                    OriginalClass = @base
                };

            @interface.Bases.AddRange(
                from b in @base.Bases
                let i = GetInterface(@base, b.Class)
                select new BaseClassSpecifier { Type = new TagType(i) });

            @interface.Methods.AddRange(
                from m in @base.Methods
                where !m.IsConstructor && !m.IsDestructor && !m.IsStatic && !m.Ignore
                select new Method(m) { Namespace = @interface });

            @interface.Properties.AddRange(
                from property in @base.Properties
                where !property.Ignore
                select new Property(property) { Namespace = @interface });

            if (@interface.Bases.Count == 0)
            {
                Property instance = new Property();
                instance.Name = Helpers.InstanceIdentifier;
                instance.QualifiedType = new QualifiedType(new BuiltinType(PrimitiveType.IntPtr));
                instance.GetMethod = new Method();
                @interface.Properties.Add(instance);
            }

            @interface.Events.AddRange(@base.Events);

            if (addMembers)
            {
                ImplementInterfaceMethods(@class, @interface);
                ImplementInterfaceProperties(@class, @interface);
            }
            if (@base.Bases.All(b => b.Class != @interface))
                @base.Bases.Add(new BaseClassSpecifier { Type = new TagType(@interface) });

            interfaces.Add(@base, @interface);
            return @interface;
        }

        private static void ImplementInterfaceMethods(Class @class, Class @interface)
        {
            foreach (var method in @interface.Methods)
            {
                var impl = new Method(method)
                    {
                        Namespace = @class,
                        IsVirtual = false,
                        IsOverride = false
                    };
                var rootBaseMethod = @class.GetRootBaseMethod(method, true);
                if (rootBaseMethod != null && !rootBaseMethod.Ignore)
                    impl.ExplicitInterfaceImpl = @interface;
                @class.Methods.Add(impl);
            }
            foreach (var @base in @interface.Bases)
                ImplementInterfaceMethods(@class, @base.Class);
        }

        private static void ImplementInterfaceProperties(Class @class, Class @interface)
        {
            foreach (var property in @interface.Properties.Where(p => p.Name != Helpers.InstanceIdentifier))
            {
                var impl = new Property(property) { Namespace = @class };
                var rootBaseProperty = @class.GetRootBaseProperty(property, true);
                if (rootBaseProperty != null && !rootBaseProperty.Ignore)
                    impl.ExplicitInterfaceImpl = @interface;
                @class.Properties.Add(impl);
            }
            foreach (var @base in @interface.Bases)
                ImplementInterfaceProperties(@class, @base.Class);
        }
    }
}