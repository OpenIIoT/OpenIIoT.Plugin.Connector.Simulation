using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using NLog;
using NLog.xLogger;
using OpenIIoT.SDK.Common.OperationResult;
using OpenIIoT.SDK.Configuration;
using OpenIIoT.SDK;
using System.Drawing;
using System.IO;
using System.Text;
using OpenIIoT.SDK.Common;
using OpenIIoT.SDK.Common.Provider.ItemProvider;
using OpenIIoT.SDK.Plugin;
using OpenIIoT.SDK.Plugin.Connector;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenIIoT.Plugin.Connector.Simulation
{
    public class ReadWriteValue
    {
        #region Private Properties

        [JsonProperty(Required = Required.Always)]
        public double Max { get; set; }

        [JsonProperty(Required = Required.Always)]
        public double Min { get; set; }

        #endregion Private Properties
    }

    /// <summary>
    ///     Provides simulation data.
    /// </summary>
    public class SimulationConnector : IConnector, ISubscribable, IConfigurable<SimulationConnectorConfiguration>, IWriteable
    {
        #region Private Fields

        /// <summary>
        ///     The main counter.
        /// </summary>
        private int counter;

        /// <summary>
        ///     The root node for the item tree.
        /// </summary>
        private Item itemRoot;

        /// <summary>
        ///     the logger for the Connector.
        /// </summary>
        private xLogger logger;

        private Random random;

        private int randomValue;
        private ReadWriteValue rwValue;

        /// <summary>
        ///     The main timer.
        /// </summary>
        private Timer timer;

        #endregion Private Fields

        #region Public Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="SimulationConnector"/> class.
        /// </summary>
        /// <param name="manager">The ApplicationManager instance.</param>
        /// <param name="instanceName">The assigned name for this instance.</param>
        /// <param name="logger">The logger for this instance.</param>
        public SimulationConnector(IApplicationManager manager, string instanceName, xLogger logger)
        {
            InstanceName = instanceName;
            this.logger = logger;

            Manager = manager;

            Name = "Simulation";
            FQN = "OpenIIoT.Plugin.Connector.Simulation";
            Version = "1.0.0.0";
            PluginType = PluginType.Connector;

            ItemProviderName = FQN;

            logger.Info("Initializing " + PluginType + " " + FQN + "." + instanceName);

            InitializeItems();

            random = new Random();
            randomValue = 0;

            rwValue = new ReadWriteValue();
            rwValue.Min = 0L;
            rwValue.Max = 100L;

            Subscriptions = new Dictionary<Item, List<Action<object>>>();

            ConfigureFileWatch();
        }

        #endregion Public Constructors

        #region Private Properties

        private IApplicationManager Manager { get; set; }

        #endregion Private Properties

        #region Public Events

        public event EventHandler<StateChangedEventArgs> StateChanged;

        #endregion Public Events

        #region Public Properties

        public bool AutomaticRestartPending { get; private set; }
        public SimulationConnectorConfiguration Configuration { get; private set; }
        public string Fingerprint { get; private set; }

        /// <summary>
        ///     The Connector FQN.
        /// </summary>
        public string FQN { get; private set; }

        /// <summary>
        ///     The name of the Connector instance.
        /// </summary>
        public string InstanceName { get; private set; }

        public string ItemProviderName { get; private set; }

        /// <summary>
        ///     The Connector name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        ///     The Connector type.
        /// </summary>
        public PluginType PluginType { get; private set; }

        /// <summary>
        ///     The State of the Connector.
        /// </summary>
        public State State { get; private set; }

        /// <summary>
        ///     The <see cref="Dictionary{TKey, TValue}"/> keyed on subscribed Item and containing a <see cref="List{T}"/> of the
        ///     <see cref="Action{T}"/> delegates used to update the subscribers.
        /// </summary>
        public Dictionary<Item, List<Action<object>>> Subscriptions { get; protected set; }

        /// <summary>
        ///     The Connector Version.
        /// </summary>
        public string Version { get; private set; }

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        ///     The GetConfigurationDefinition method is static and returns the ConfigurationDefinition for the Endpoint.
        ///
        ///     This method is necessary so that the configuration defintion can be registered with the ConfigurationManager prior
        ///     to any instances being created. This method MUST be implemented, however it is not possible to specify static
        ///     methods in an interface, so implementing IConfigurable will not enforce this.
        /// </summary>
        /// <returns>The ConfigurationDefinition for the Endpoint.</returns>
        public static IConfigurationDefinition GetConfigurationDefinition()
        {
            ConfigurationDefinition retVal = new ConfigurationDefinition();

            // to create the form and schema strings, visit http://schemaform.io/examples/bootstrap-example.html use the example to
            // create the desired form and schema, and ensure that the resulting model matches the model for the endpoint. When you
            // are happy with the json from the above url, visit http://www.freeformatter.com/json-formatter.html#ad-output and
            // paste in the generated json and format it using the "JavaScript escaped" option. Paste the result into the methods below.

            retVal.Form = "[\"templateURL\",{\"type\":\"submit\",\"style\":\"btn-info\",\"title\":\"Save\"}]";
            retVal.Schema = "{\"type\":\"object\",\"title\":\"XMLEndpoint\",\"properties\":{\"templateURL\":{\"title\":\"Template URL\",\"type\":\"string\"}},\"required\":[\"templateURL\"]}";

            // this will always be typeof(YourConfiguration/ModelObject)
            retVal.Model = typeof(SimulationConnectorConfiguration);

            SimulationConnectorConfiguration config = new SimulationConnectorConfiguration();
            config.Interval = 100;

            retVal.DefaultConfiguration = config;

            return retVal;
        }

        public Item Browse()
        {
            return itemRoot;
        }

        public IList<Item> Browse(Item root)
        {
            return (root == null ? itemRoot.Children : root.Children);
        }

        public async Task<Item> BrowseAsync()
        {
            return await Task.Run(() => Browse());
        }

        public async Task<IList<Item>> BrowseAsync(Item root)
        {
            return await Task.Run(() => Browse(root));
        }

        /// <summary>
        ///     The parameterless Configure() method calls the overloaded Configure() and passes in the instance of the model/type
        ///     returned by the GetConfiguration() method in the Configuration Manager.
        ///
        ///     This is akin to saying "configure yourself using whatever is in the config file"
        /// </summary>
        /// <returns></returns>
        public IResult Configure()
        {
            logger.EnterMethod();
            logger.Debug("Attempting to Configure with the configuration from the Configuration Manager...");
            Result retVal = new Result();

            IResult<SimulationConnectorConfiguration> fetchResult = Manager.GetManager<IConfigurationManager>().Configuration.GetInstance<SimulationConnectorConfiguration>(GetType());

            // if the fetch succeeded, configure this instance with the result.
            if (fetchResult.ResultCode != ResultCode.Failure)
            {
                logger.Debug("Successfully fetched the configuration from the Configuration Manager.");
                Configure(fetchResult.ReturnValue);
            }
            else
            {
                // if the fetch failed, add a new default instance to the configuration and try again.
                logger.Debug("Unable to fetch the configuration.  Adding the default configuration to the Configuration Manager...");
                IResult<SimulationConnectorConfiguration> createResult = Manager.GetManager<IConfigurationManager>().Configuration.AddInstance<SimulationConnectorConfiguration>(GetType(), GetConfigurationDefinition().DefaultConfiguration);
                if (createResult.ResultCode != ResultCode.Failure)
                {
                    logger.Debug("Successfully added the configuration.  Configuring...");
                    Configure(createResult.ReturnValue);
                }

                retVal.Incorporate(createResult);
            }

            retVal.LogResult(logger.Debug);
            logger.ExitMethod(retVal);
            return retVal;
        }

        /// <summary>
        ///     The Configure method is called by external actors to configure or re-configure the Endpoint instance.
        ///
        ///     If anything inside the Endpoint needs to be refreshed to reflect changes to the configuration, do it in this method.
        /// </summary>
        /// <param name="configuration">The instance of the model/configuration type to apply.</param>
        /// <returns>A Result containing the result of the operation.</returns>
        public IResult Configure(SimulationConnectorConfiguration configuration)
        {
            Configuration = configuration;

            return new Result();
        }

        public Item Find(string fqn)
        {
            return Find(itemRoot, fqn);
        }

        public async Task<Item> FindAsync(string fqn)
        {
            return await Task.Run(() => Find(fqn));
        }

        /// <summary>
        ///     Returns true if any of the specified <see cref="State"/> s match the current <see cref="State"/>.
        /// </summary>
        /// <param name="states">The list of States to check.</param>
        /// <returns>True if the current State matches any of the specified States, false otherwise.</returns>
        public virtual bool IsInState(params State[] states)
        {
            return states.Any(s => s == State);
        }

        public object Read(Item item)
        {
            object retVal = new object();

            double val = DateTime.Now.Second;
            double ms = DateTime.Now.Millisecond / 10;
            double count = (double)counter / 10;
            switch (item.FQN.Split('.')[item.FQN.Split('.').Length - 1])
            {
                case "Read":
                    retVal = rwValue;
                    return retVal;

                case "Write":
                    retVal = rwValue;
                    return retVal;

                case "Sine":
                    retVal = Math.Sin(counter / 10l);
                    return retVal;

                case "Cosine":
                    retVal = Math.Cos(val);
                    return retVal;

                case "Tangent":
                    retVal = Math.Tan(val);
                    return retVal;

                case "Trig":
                    retVal = new double[4] { count, Math.Sin(count), Math.Cos(count), Math.Tan(count) };
                    return retVal;

                case "Ramp":
                    retVal = val;
                    return retVal;

                case "Random":
                    randomValue += random.Next(-1, 2) * random.Next(1, 3);

                    if (randomValue < 0) randomValue = 0;
                    if (randomValue > 100) randomValue = 100;

                    return randomValue;

                case "Step":
                    retVal = val % 5;
                    return retVal;

                case "Toggle":
                    retVal = val % 2;
                    return retVal;

                case "Time":
                    retVal = DateTime.Now.ToString("HH:mm:ss.fff");
                    return retVal;

                case "Date":
                    retVal = DateTime.Now.ToString("MM/dd/yyyy");
                    return retVal;

                case "TimeZone":
                    retVal = DateTime.Now.ToString("zzz");
                    return retVal;

                case "Array":
                    retVal = new int[5] { 1, 2, 3, 4, 5 };
                    return retVal;

                case "StaticImage":
                    retVal = GetStaticImage();
                    return retVal;

                case "DynamicImage":
                    retVal = GetDynamicImage();
                    return retVal;

                case "MousePosition":
                    retVal = GetCursorPosition();
                    return retVal;

                default:
                    return retVal;
            }

            logger.Info("Returning: " + JsonConvert.SerializeObject(retVal));
        }

        public async Task<object> ReadAsync(Item item)
        {
            return await Task.Run(() => Read(item));
        }

        public IResult Restart(StopType stopType = StopType.Stop)
        {
            return Start().Incorporate(Stop(stopType | StopType.Restart));
        }

        public IResult SaveConfiguration()
        {
            return Manager.GetManager<IConfigurationManager>().Configuration.UpdateInstance(this.GetType(), Configuration);
        }

        public void SetFingerprint(string fingerprint)
        {
            Fingerprint = fingerprint;
        }

        public IResult Start()
        {
            Configure();

            counter = 0;
            timer = new Timer(Configuration.Interval);
            timer.Elapsed += Timer_Elapsed;

            timer.Start();
            return new Result();
        }

        public IResult Stop(StopType stopType = StopType.Stop)
        {
            timer.Stop();
            return new Result();
        }

        /// <summary>
        ///     Creates a subscription to the specified Item.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Upon the addition of the initial subscriber, an entry is added to the <see cref="Subscriptions"/> Dictionary
        ///         keyed with the specified Item with a new <see cref="List{T}"/> of type <see cref="Action{T}"/> containing one
        ///         entry corresponding to the specified callback delegate.
        ///     </para>
        ///     <para>
        ///         Successive additions add each of the specified callback delegates to the <see cref="Subscriptions"/> dictionary.
        ///     </para>
        /// </remarks>
        /// <param name="item">The <see cref="Item"/> to which the subscription should be added.</param>
        /// <param name="callback">The callback delegate to be invoked upon change of the subscribed Item.</param>
        /// <returns>A value indicating whether the operation succeeded.</returns>
        public bool Subscribe(Item item, Action<object> callback)
        {
            bool retVal = false;

            try
            {
                if (!Subscriptions.ContainsKey(item))
                {
                    Subscriptions.Add(item, new List<Action<object>>());
                }

                Subscriptions[item].Add(callback);

                retVal = true;
            }
            catch (Exception ex)
            {
                logger.Exception(ex);
            }

            return retVal;
        }

        /// <summary>
        ///     Removes a subscription from the specified ConnectorItem.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Upon the removal of a subscriber the specified callback delegate is removed from the corresponding Dictionary
        ///         entry for the specified <see cref="Item"/>.
        ///     </para>
        ///     Upon removal of the final subscriber, the Dictionary key corresponding to the specified <see cref="Item"/> is
        ///     completely removed.
        /// </remarks>
        /// <param name="item">The <see cref="Item"/> for which the subscription should be removed.</param>
        /// <param name="callback">The callback delegate to be invoked upon change of the subscribed Item.</param>
        /// <returns>A value indicating whether the operation succeeded.</returns>
        public bool UnSubscribe(Item item, Action<object> callback)
        {
            bool retVal = false;

            try
            {
                if (Subscriptions.ContainsKey(item))
                {
                    Subscriptions[item].Remove(callback);

                    if (Subscriptions[item].Count == 0)
                    {
                        Subscriptions.Remove(item);
                    }

                    retVal = true;
                }
            }
            catch (Exception ex)
            {
                logger.Exception(ex);
            }

            return retVal;
        }

        public bool Write(Item item, object value)
        {
            bool retVal = default(bool);

            try
            {
                string obj = ((JObject)value).ToString(Formatting.None);
                logger.Info("Write: " + item + " value: " + obj);

                rwValue = JsonConvert.DeserializeObject<ReadWriteValue>(obj, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Error, CheckAdditionalContent = true });

                // will fire the Changed event which will cascade the value through the model
                foreach (Item key in Subscriptions.Keys)
                {
                    if (key.FQN.Contains("ReadWrite.Read"))
                    {
                        foreach (Action<object> callback in Subscriptions[key])
                        {
                            callback.Invoke(rwValue);
                        }
                    }
                }

                retVal = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
            }

            return retVal;
        }

        public async Task<bool> WriteAsync(Item item, object value)
        {
            return await Task.Run(() => Write(item, value));
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        ///     Retrieves the cursor's position, in screen coordinates.
        /// </summary>
        /// <see>See MSDN documentation for further information.</see>
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private static Point GetCursorPosition()
        {
            POINT lpPoint;
            GetCursorPos(out lpPoint);
            //bool success = User32.GetCursorPos(out lpPoint);
            // if (!success)

            return lpPoint;
        }

        private void ConfigureFileWatch()
        {
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetAssembly(GetType()).Location);
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Filter = "image.jpg";
            watcher.Changed += new FileSystemEventHandler(OnDynamicImageChange);
            watcher.EnableRaisingEvents = true;
        }

        private Item Find(Item root, string fqn)
        {
            if (root.FQN == fqn) return root;

            Item found = default(Item);
            foreach (Item child in root.Children)
            {
                found = Find(child, fqn);
                if (found != default(Item)) break;
            }
            return found;
        }

        private byte[] GetDynamicImage()
        {
            string fullPath = Path.GetDirectoryName(System.Reflection.Assembly.GetAssembly(GetType()).Location);
            string fileName = Path.Combine(fullPath, "image.jpg");

            if (File.Exists(fileName))
            {
                return ReadFile(fileName);
            }
            else
            {
                string err = "Not found: " + fileName;
                return Encoding.ASCII.GetBytes(err);
            }
        }

        private byte[] GetStaticImage()
        {
            return ImageToByteArray(Properties.Resources.OpenIIoT);
        }

        private byte[] ImageToByteArray(Image image)
        {
            ImageConverter converter = new ImageConverter();
            return (byte[])converter.ConvertTo(image, typeof(byte[]));
        }

        private void InitializeItems()
        {
            // instantiate an item root
            itemRoot = new Item(InstanceName, this);

            // create some simulation items
            Item mathRoot = itemRoot.AddChild(new Item("Math", this)).ReturnValue;
            mathRoot.AddChild(new Item("Trig", this));
            mathRoot.AddChild(new Item("Sine", this));
            mathRoot.AddChild(new Item("Cosine", this));
            mathRoot.AddChild(new Item("Tangent", this));

            Item processRoot = itemRoot.AddChild(new Item("Process", this)).ReturnValue;
            processRoot.AddChild(new Item("Ramp", this));
            processRoot.AddChild(new Item("Step", this));
            processRoot.AddChild(new Item("Toggle", this));
            processRoot.AddChild(new Item("Random", this));

            Item timeRoot = itemRoot.AddChild(new Item("DateTime", this)).ReturnValue;
            timeRoot.AddChild(new Item("Time", this));
            timeRoot.AddChild(new Item("Date", this));
            timeRoot.AddChild(new Item("TimeZone", this));

            Item binaryRoot = itemRoot.AddChild(new Item("Binary", this)).ReturnValue;
            binaryRoot.AddChild(new Item("StaticImage", this));
            binaryRoot.AddChild(new Item("DynamicImage", this));

            Item miscRoot = itemRoot.AddChild(new Item("Misc", this)).ReturnValue;
            miscRoot.AddChild(new Item("MousePosition", this));

            Item rwRoot = itemRoot.AddChild(new Item("ReadWrite", this)).ReturnValue;
            rwRoot.AddChild(new Item("Read", this));
            rwRoot.AddChild(new Item("Write", this));
        }

        private void OnDynamicImageChange(object sender, FileSystemEventArgs args)
        {
            logger.Info("File watcher for " + args.FullPath);

            Item key = Find("Simulation.Binary.DynamicImage");
            if (Subscriptions.ContainsKey(key))
            {
                foreach (Action<object> callback in Subscriptions[key])
                {
                    callback.Invoke(ReadFile(args.FullPath));
                    logger.Info("Invoked dynamic image change delegate");
                }
            }
        }

        private byte[] ReadFile(string fileName)
        {
            byte[] retVal = default(byte[]);

            while (true)
            {
                try
                {
                    retVal = File.ReadAllBytes(fileName);
                    break;
                }
                catch (IOException ex)
                {
                    logger.Info("Deferred read due to " + ex.GetType().Name);
                    System.Threading.Thread.Sleep(10);
                }
            }

            return retVal;
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            counter++;

            // iterate over the subscribed tags and update them using Write() this will update the value of the ConnectorItem and
            // will fire the Changed event which will cascade the value through the model
            foreach (Item key in Subscriptions.Keys)
            {
                //if (key.FQN == InstanceName + ".DateTime.Time") key.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
                //if (key.FQN == InstanceName + ".Process.Ramp") key.Write(counter);
                if (key.FQN.Contains("DateTime.Time"))
                {
                    foreach (Action<object> callback in Subscriptions[key])
                    {
                        callback.Invoke(DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                }
                if (key.FQN.Contains("Math.Sine") || key.FQN.Contains("Math.Trig") || key.FQN.Contains("Misc.MousePosition") || key.FQN.Contains("Random"))
                {
                    foreach (Action<object> callback in Subscriptions[key])
                    {
                        try
                        {
                            callback.Invoke(Read(key));
                        }
                        catch (Exception ex)
                        {
                            //logger.Error("Error executing callback for subscription to " + key.FQN + "; " + ex.Message);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Struct representing a point.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public static implicit operator Point(POINT point)
            {
                return new Point(point.X, point.Y);
            }
        }

        #endregion Private Methods
    }

    public class SimulationConnectorConfiguration
    {
        #region Public Properties

        public int Interval { set; get; }

        #endregion Public Properties
    }
}