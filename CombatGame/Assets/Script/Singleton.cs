using UnityEngine;

namespace Footsies
{

    /// <summary>
    /// Be aware this will not prevent a non singleton constructor
    ///   such as `T myT = new T();`
    /// To prevent that, add `protected T () {}` to your singleton class.
    /// 
    /// As a note, this is made as MonoBehaviour because we need Coroutines.
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        protected static T _instance;

        private static object _lock = new object();

        public static bool HasInstance => _instance != null;

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    Debug.LogWarning("[Singleton] Instance '" + typeof(T) +
                        "' already destroyed on application quit." +
                        " Won't create again - returning null.");
                    return null;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        var instances = FindObjectsOfType<T>();
                        if (instances.Length > 0)
                        {
                            _instance = instances[0];
                            if (instances.Length > 1)
                            {
                                Debug.LogWarning("[Singleton] Multiple instances of '" + typeof(T) +
                                    "' found in the scene! Using the first one and destroying duplicates.");
                                for (int i = 1; i < instances.Length; i++)
                                {
                                    if (instances[i] != null && instances[i].gameObject != null)
                                    {
                                        Destroy(instances[i].gameObject);
                                    }
                                }
                            }
                            return _instance;
                        }

                        if (_instance == null)
                        {
                            GameObject singleton = new GameObject();
                            _instance = singleton.AddComponent<T>();
                            singleton.name = "(singleton) " + typeof(T).ToString();

                            DontDestroyOnLoad(singleton);

                            Debug.Log("[Singleton] An instance of " + typeof(T) +
                                " is needed in the scene, so '" + singleton +
                                "' was created with DontDestroyOnLoad.");
                        }
                        else
                        {
                            Debug.Log("[Singleton] Using instance already created: " +
                                _instance.gameObject.name);
                        }
                    }

                    return _instance;
                }
            }
        }

        protected static bool applicationIsQuitting = false;

        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        public virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}