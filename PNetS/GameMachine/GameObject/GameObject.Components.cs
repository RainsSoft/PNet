﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Yaml.Serialization;

namespace PNetS
{
    public sealed partial class GameObject
    {
        /// <summary>
        /// The name of the method that gets run right after a component is instantiated
        /// </summary>
        public const string AwakeMethodName = "Awake";
        /// <summary>
        /// The name of the method that gets run once, right before the first time it runs Update
        /// </summary>
        public const string StartMethodName = "Start";
        /// <summary>
        /// The name of the method that gets run for updates
        /// </summary>
        public const string UpdateMethodName = "Update";
        /// <summary>
        /// The name of the method that gets run for lateUpdate
        /// </summary>
        public const string LateUpdateMethodName = "LateUpdate";
        /// <summary>
        /// The name of the method that gets run when a client connects
        /// </summary>
        public const string OnPlayerConnectedMethodName = "OnPlayerConnected";
        /// <summary>
        /// The name of the method that gets run when a client disconnects
        /// </summary>
        public const string OnPlayerDisconnectedMethodName = "OnPlayerDisconnected";
        /// <summary>
        /// The name of the method that gets run when a client leaves the room the gameobject is in
        /// </summary>
        public const string OnPlayerLeftRoomMethodName = "OnPlayerLeftRoom";
        /// <summary>
        /// The name of the method that gets run when a client joins the room the gameobject is in
        /// gets run before the player finishes instantiating things
        /// </summary>
        public const string OnPlayerEnteredRoomMethodName = "OnPlayerEnteredRoom";
        /// <summary>
        /// Name of the method that gets run when a component is added
        /// </summary>
        public const string OnComponentAddedMethodName = "OnComponentAdded";
        /// <summary>
        /// Name of the method that gets run when the ack for instantiation is received
        /// </summary>
        public const string OnInstantiationFinishedMethodName = "OnInstantiationFinished";
        /// <summary>
        /// Name of the method that gets run before the object gets destroyed
        /// </summary>
        public const string OnDestroyMethodName = "OnDestroy";

        internal class ComponentTracker
        {
            public Component component;
            internal Action update;
            internal Action lateUpdate;
            internal Action<Player> onPlayerConnected;
            internal Action<Player> onPlayerDisconnected;
            internal Action<Component> onComponentAdded;
            internal Action<Player> onFinishedInstantiate;
            internal Action onDestroy;
            internal Action<Player> onPlayerLeftRoom;
            internal Action<Player> onPlayerEnteredRoom;
        }

        [YamlSerialize(YamlSerializeMethod.Assign)] 
        private List<ComponentTracker> components = new List<ComponentTracker>(4);

        internal void OnComponentAfterDeserialization()
        {
            foreach (var component in components)
            {
                OnComponentAdded(component.component);
            }
        }
        
        /// <summary>
        /// Get the first component of type T attached to the gameobject
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetComponent<T>() 
            where T : class
        {
            if (components == null)
                return null;

            return components.Select(c => c.component).OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// Get the component of the specified type
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Component GetComponent(Type t)
        {
            return (from c in components where c.component.GetType().IsSubclassOf(t) select c.component).FirstOrDefault();
        }

        /// <summary>
        /// Get all the components of type T attached to the gameObject
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public List<T> GetComponents<T>() 
            where T : class
        {
            return components.Select(c => c.component).OfType<T>().ToList();
        }

        /// <summary>
        /// Attach the component of type T to the gameobject
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>instance of T that was attached to the gameObject</returns>
        public T AddComponent<T>() 
            where T : Component, new()
        {
            var component = new T();

            var awakeMethod = AddComponentToGameObject(component, new ComponentTracker());
            if (awakeMethod != null)
            {
                try { awakeMethod(); }
                catch (Exception e) { Debug.LogError(e.Message); }
            }

            OnComponentAdded(component);
            return component;
        }

        /// <summary>
        /// Add a component of the specified type to the gameObject
        /// </summary>
        /// <param name="componentType">Must be an inheriting class of Component</param>
        /// <returns>instance of the component that was added</returns>
        public Component AddComponent(Type componentType)
        {
            var component = Activator.CreateInstance(componentType) as Component;

            if (component == null)
            {
                Debug.LogError("Attempted to add a type that was not of the type Component to the gameobject");
                return null;
            }

            var awakeMethod = AddComponentToGameObject(component, new ComponentTracker());
            if (awakeMethod != null)
            {
                try { awakeMethod(); }
                catch (Exception e) { Debug.LogError(e.Message); }
            }

            OnComponentAdded(component);
            return component;
        }

        internal Component DeserializeAddComponent(Type componentType, out Action awakeMethod, ComponentTracker tracker)
        {
            var component = Activator.CreateInstance(componentType) as Component;
            awakeMethod = null;

            if (component == null)
            {
                Debug.LogError("Attempted to add a type that was not of the type Component to the gameobject");
                return null;
            }

            awakeMethod = AddComponentToGameObject(component, tracker);

            return component;
        }

        /// <summary>
        /// Add the specified types of components to the gameobject.
        /// Awake is run after all the components have been initialized, so you can safely do GetComponent on other types passed into this during Awake
        /// The awake order is also run in the order at which the components are ordered in the parameters
        /// </summary>
        /// <param name="componentTypes"></param>
        /// <returns></returns>
        public Component[] AddComponents(params Type[] componentTypes)
        {
            var length = componentTypes.Length;
            var awakes = new Action[length];
            var addedComponents = new Component[length];
            for (int i = 0; i < length; ++i)
            {
                Component component = Activator.CreateInstance(componentTypes[i]) as Component;
                if (component == null)
                {
                    Debug.LogError("Attempted to add a type that was not of the type Component to the gameobject");
                }
                else
                {
                    addedComponents[i] = component;
                    awakes[i] = AddComponentToGameObject(component, new ComponentTracker());
                }
            }

            for (int i = 0; i < length; ++i)
            {
                var awake = awakes[i];
                if (awake != null)
                    try { awake(); }
                    catch (Exception e) { Debug.LogError(e.Message); }
            }

            return addedComponents;
        }

        private Action AddComponentToGameObject(Component component, ComponentTracker tracker)
        {
            component.gameObject = this;

            tracker.component = component;

            var methods = GetMethods(
                component,
                new List<string>()
                    {
                        StartMethodName,
                        UpdateMethodName, 
                        LateUpdateMethodName,
                        OnPlayerConnectedMethodName,
                        OnPlayerDisconnectedMethodName,
                        OnComponentAddedMethodName,
                        AwakeMethodName,
                        OnInstantiationFinishedMethodName,
                        OnDestroyMethodName,
                        OnPlayerLeftRoomMethodName,
                        OnPlayerEnteredRoomMethodName
                    },
                new List<Type>()
                    {
                        typeof(Action),
                        typeof(Action),
                        typeof(Action),
                        typeof(Action<Player>),
                        typeof(Action<Player>),
                        typeof(Action<Component>),
                        typeof(Action),
                        typeof(Action<Player>),
                        typeof(Action),
                        typeof(Action<Player>),
                        typeof(Action<Player>)
                    });

            var startMethod = methods[0] as Action;
            tracker.update = methods[1] as Action;
            tracker.lateUpdate = methods[2] as Action;
            tracker.onPlayerConnected = methods[3] as Action<Player>;
            tracker.onPlayerDisconnected = methods[4] as Action<Player>;
            tracker.onComponentAdded = methods[5] as Action<Component>;
            var awakeMethod = methods[6] as Action;
            tracker.onFinishedInstantiate = methods[7] as Action<Player>;
            tracker.onDestroy = methods[8] as Action;
            tracker.onPlayerLeftRoom = methods[9] as Action<Player>;
            tracker.onPlayerEnteredRoom = methods[10] as Action<Player>;

            components.Add(tracker);

            if (startMethod != null)
            {
                GameState.AddStart(startMethod);
            }

            return awakeMethod;
        }


        /// <summary>
        /// Remove the specified component. Only by actual object.Reference
        /// </summary>
        /// <param name="target"></param>
        public void RemoveComponent(Component target)
        {
            var ind = components.FindIndex(c => object.ReferenceEquals(c.component, target));

            if (ind != -1)
            {
                components.RemoveAt(ind);
                target.Dispose();
            }
        }

        private object[] GetMethods(object target, List<string> methodNames, List<Type> methodTypes)
        {
            if (methodNames.Count != methodTypes.Count)
                throw new ArgumentOutOfRangeException("methodTypes", "methodNames and methodTypes must be the same length");

            MethodInfo[] methods = target.GetType().GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.FlattenHierarchy);

            object[] retMethods = new object[methodNames.Count];
            
            foreach (var method in methods)
            {
                var ind = methodNames.IndexOf(method.Name);
                if (ind != -1)
                {
                    retMethods[ind] = Delegate.CreateDelegate(methodTypes[ind], target, method, false);
                }
            }

            return retMethods;
        }

        private T GetActionMethod<T>(object target, string methodName) 
            where T : class
        {
            MethodInfo method = target.GetType()
            .GetMethod(methodName,
                       BindingFlags.Public
                       | BindingFlags.NonPublic
                       | BindingFlags.Instance
                       | BindingFlags.FlattenHierarchy);

            if (method == null)
                return null;

            return Delegate.CreateDelegate
                (typeof(T), target, method, false) as T;

        }
    }
}
