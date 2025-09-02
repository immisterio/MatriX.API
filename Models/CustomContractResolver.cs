using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Reflection;

public class CustomContractResolver : DefaultContractResolver
{
    private readonly Type settingType;
    private readonly Type serverType;
    private readonly Type serverLimitType;
    private readonly Type rateLimitType;
    private readonly object defaultSetting;
    private readonly object defaultServer;
    private readonly object defaultServerLimit;
    private readonly object defaultRateLimit;

    public CustomContractResolver(Type settingType, Type serverType, Type serverLimitType, Type rateLimitType)
    {
        this.settingType = settingType;
        this.serverType = serverType;
        this.serverLimitType = serverLimitType;
        this.rateLimitType = rateLimitType;
        defaultSetting = Activator.CreateInstance(settingType);
        defaultServer = Activator.CreateInstance(serverType);
        defaultServerLimit = Activator.CreateInstance(serverLimitType);
        defaultRateLimit = Activator.CreateInstance(rateLimitType);
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var prop = base.CreateProperty(member, memberSerialization);

        prop.ShouldSerialize = instance =>
        {
            var value = prop.ValueProvider.GetValue(instance);

            // null
            if (value == null)
                return false;

            // false
            if (value is bool b && b == false)
                return false;

            // пустой массив/список
            if (value is IEnumerable enumerable && !(value is string))
            {
                var enumerator = enumerable.GetEnumerator();
                if (!enumerator.MoveNext())
                    return false;
            }

            // Получаем дефолтное значение свойства
            object defaultValue = null;
            if (instance != null)
            {
                PropertyInfo pi = member as PropertyInfo;
                if (pi != null)
                {
                    if (settingType.IsInstanceOfType(instance))
                        defaultValue = pi.GetValue(defaultSetting);
                    else if (serverType.IsInstanceOfType(instance))
                        defaultValue = pi.GetValue(defaultServer);
                    else if (serverLimitType.IsInstanceOfType(instance))
                        defaultValue = pi.GetValue(defaultServerLimit);
                    else if (rateLimitType.IsInstanceOfType(instance))
                        defaultValue = pi.GetValue(defaultRateLimit);
                }
            }

            if (defaultValue != null && Equals(value, defaultValue))
                return false;

            return true;
        };

        return prop;
    }
}