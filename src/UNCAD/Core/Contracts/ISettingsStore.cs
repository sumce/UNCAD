namespace UNCAD.Core.Contracts
{
    /// <summary>配置存储抽象。实现：Infra.RegistrySettingsStore（注册表，键名兼容旧 LISP 插件）。</summary>
    public interface ISettingsStore
    {
        string Get(string name, string defaultValue);
        void Set(string name, string value);
    }
}
