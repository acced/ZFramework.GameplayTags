// Compile-only substitutes for the small Unity component adapter. These do not
// simulate Unity lifecycle, serialization, the Editor, or IL2CPP execution.
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute { }
    public interface ISerializationCallbackReceiver
    {
        void OnBeforeSerialize();
        void OnAfterDeserialize();
    }
    public class MonoBehaviour { }
    public class GameObject
    {
        public T GetComponent<T>() where T : class { return null; }
        public T AddComponent<T>() where T : new() { return new T(); }
    }
}
