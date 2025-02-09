﻿namespace PACommon.Extensions
{
    /// <summary>
    /// Object for storing extensions for <c>Type</c>.
    /// </summary>
    public static class TypeExtensions
    {
        /// <inheritdoc cref="Type.IsAssignableFrom(Type?)"/>
        public static bool IsGenericAssignableFromType(this Type genericType, Type givenType)
        {
            var interfaceTypes = givenType.GetInterfaces();

            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == genericType)
                    return true;
            }

            if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
                return true;

            Type? baseType = givenType.BaseType;
            if (baseType == null) return false;

            return genericType.IsGenericAssignableFromType(baseType);
        }
    }
}
