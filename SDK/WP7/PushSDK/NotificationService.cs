﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using Coding4Fun.Phone.Controls;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Notification;
using Microsoft.Phone.Tasks;
using PushSDK.Classes;
using PushSDK.Controls;

namespace PushSDK
{
    public class NotificationService
    {
        #region private fields
        private readonly string _pushPage;

        private readonly Collection<Uri> _tileTrustedServers;

        private HttpNotificationChannel _notificationChannel;

        private RegistrationService _registrationService;


        #endregion

        #region public properties

        /// <summary>
        /// Get push chanell uri
        /// </summary>
        public Uri ChannelUri
        {
            get { return _notificationChannel.ChannelUri; }
        }

        /// <summary>
        /// Get application ID at server
        /// </summary>
        public string AppID { get; private set; }

        /// <summary>
        /// Get content of last push notification
        /// </summary>
        public string LastPushContent
        {
            get
            {
                return LastPush != null ? LastPush.Contnet : string.Empty;
            }
        }

        /// <summary>
        /// Get services for sending tags
        /// </summary>
        public TagsService Tags { get; private set; }

        /// <summary>
        /// Get user data from the last push came
        /// </summary>
        public string UserData { get{return LastPush != null ? LastPush.UserData : string.Empty;}}

        /// <summary>
        /// Get a service to manage Geozone
        /// </summary>
        public GeozoneService GeoZone { get; private set; }

        #endregion

        #region internal properties

        internal StatisticService Statistic { get; set; }

        internal ToastPush LastPush { get; set; }

        #endregion

        #region public events

        /// <summary>
        /// User wants to see push
        /// </summary>
        public event EventHandler<CustomEventArgs<string>> OnPushAccepted;

        /// <summary>
        /// Push channel error occurred
        /// </summary>
        public event EventHandler<NotificationChannelErrorEventArgs> OnChannelErrorOccurred;

        /// <summary>
        /// Uri of push channel was updated
        /// </summary>
        public event EventHandler<NotificationChannelUriEventArgs> OnChannelUriUpdated;

        #endregion

        #region Singleton

        private static NotificationService _instance;

        public static NotificationService GetCurrent(string appID, string pushPage, IEnumerable<string> tileTrustedServers)
        {
            return _instance ?? (_instance = tileTrustedServers == null ? new NotificationService(appID, pushPage) : new NotificationService(appID, pushPage, tileTrustedServers));
        }

        #endregion

        /// <param name="appID">PushWoosh application id</param>
        /// <param name="pushPage">Page on which the navigation is when receiving toast push notification </param>
        private NotificationService(string appID, string pushPage)
        {
            _pushPage = pushPage;
            AppID = appID;

            Statistic = new StatisticService(appID);
            Tags = new TagsService(appID);
            GeoZone = new GeozoneService(appID);
        }

        /// <param name="appID">PushWoosh application id</param>
        /// <param name="pushPage">Page on which the navigation is when receiving toast push notification </param>
        /// <param name="tileTrustedServers">Uris of trusted servers for tile images</param>
        private NotificationService(string appID, string pushPage, IEnumerable<string> tileTrustedServers)
            : this(appID, pushPage)
        {
            _tileTrustedServers = new Collection<Uri>(tileTrustedServers.Select(s => new Uri(s, UriKind.Absolute)).ToList());
        }

        #region public methods

        /// <summary>
        /// Creates push channel and regestrite it at pushwoosh server to send unauthenticated pushes
        /// </summary>
        public void SubscribeToPushService()
        {
            SubscribeToPushService(string.Empty);
        }

        /// <summary>
        /// Creates push channel and regestrite it at pushwoosh server
        /// <param name="serviceName">
        /// The name that the web service uses to associate itself with the Push Notification Service.
        /// </param>
        /// </summary>        
        public void SubscribeToPushService(string serviceName)
        {
            //First, try to pick up existing channel
            _notificationChannel = HttpNotificationChannel.Find(Constants.ChannelName);

            if (_notificationChannel != null)
            {
                Debug.WriteLine("Channel Exists - no need to create a new one");
                SubscribeToChannelEvents();

                Debug.WriteLine("Register the URI with 3rd party web service. URI is:" + _notificationChannel.ChannelUri);
                SubscribeToService(AppID);

                Debug.WriteLine("Subscribe to the channel to Tile and Toast notifications");
                SubscribeToNotifications();
            }
            else
            
            
            
            
            
            
            
            
            {
                Debug.WriteLine("Trying to create a new channel...");
                if (string.IsNullOrEmpty(serviceName)) _notificationChannel = new HttpNotificationChannel(Constants.ChannelName);
                else _notificationChannel = new HttpNotificationChannel(Constants.ChannelName, serviceName);

                Debug.WriteLine("New Push Notification channel created successfully");

                SubscribeToChannelEvents();

                Debug.WriteLine("Trying to open the channel");
                _notificationChannel.Open();
            }
        }

        /// <summary>
        /// Unsubscribe from pushes at pushwoosh server
        /// </summary>
        public void UnsubscribeFromPushes()
        {
            if (_registrationService == null) return;
            _notificationChannel.UnbindToShellTile();
            _notificationChannel.UnbindToShellToast();
            _registrationService.Unregister();
        }

        #endregion

        #region private methods

        private void SubscribeToService(string appID)
        {
            if (_registrationService == null)
                _registrationService = new RegistrationService();

            _registrationService.Register(appID, _notificationChannel.ChannelUri);
        }

        private void SubscribeToChannelEvents()
        {
            //Register to UriUpdated event - occurs when channel successfully opens
            _notificationChannel.ChannelUriUpdated += Channel_ChannelUriUpdated;

            //general error handling for push channel
            _notificationChannel.ErrorOccurred += Channel_ErrorOccurred;

            _notificationChannel.ShellToastNotificationReceived += ChannelShellToastNotificationReceived;
        }

        private void ChannelShellToastNotificationReceived(object sender, NotificationEventArgs e)
        {
            Debug.WriteLine("/********************************************************/");
            Debug.WriteLine("Received Toast: " + DateTime.Now.ToShortTimeString());

            foreach (string key in e.Collection.Keys)
            {
                Debug.WriteLine("{0}: {1}", key, e.Collection[key]);
                if (key == "wp:Param")
                    LastPush = SDKHelpers.ParsePushData(e.Collection[key]);
            }

            Deployment.Current.Dispatcher.BeginInvoke(() =>
                                                          {
                                                              var message = new PushNotificationMessage(e.Collection);
                                                              message.Completed += (o, args) =>
                                                                                       {
                                                                                           if (args.PopUpResult == PopUpResult.Ok)
                                                                                                   FireAcceptedPush();
                                                                                       };
                                                              message.Show();
                                                          });
        }

        internal void FireAcceptedPush()
        {
            Statistic.SendRequest();
            if (LastPush.Url != null || LastPush.HtmlId != -1)
            {
                WebBrowserTask webBrowserTask = new WebBrowserTask();

                if (LastPush.Url != null)
                    webBrowserTask.Uri = LastPush.Url;
                else if (LastPush.HtmlId != -1)
                    webBrowserTask.Uri = new Uri(Constants.HtmlPageUrl + LastPush.HtmlId, UriKind.Absolute);

                webBrowserTask.Show();
            }

            if (!string.IsNullOrEmpty(_pushPage) && Application.Current.RootVisual is PhoneApplicationFrame)
            {
                ((PhoneApplicationFrame) Application.Current.RootVisual).Navigated += OnNavigated;
                ((PhoneApplicationFrame) Application.Current.RootVisual).Navigate(new Uri(_pushPage, UriKind.Relative));
            }
            else
                PushAccepted();

        }

        private void OnNavigated(object sender, NavigationEventArgs navigationEventArgs)
        {
            PushAccepted();
            ((PhoneApplicationFrame) Application.Current.RootVisual).Navigated -= OnNavigated;
        }

        private void PushAccepted()
        {
            if (OnPushAccepted != null)
                OnPushAccepted(this, new CustomEventArgs<string> {Result = LastPushContent});
        }

        private void Channel_ErrorOccurred(object sender, NotificationChannelErrorEventArgs e)
        {
            Debug.WriteLine("/********************************************************/");
            Debug.WriteLine("A push notification {0} error occurred.  {1} ({2}) {3}", e.ErrorType, e.Message, e.ErrorCode, e.ErrorAdditionalData);

            if (OnChannelErrorOccurred != null)
                OnChannelErrorOccurred(sender, e);
        }

        private void SubscribeToNotifications()
        {
            try
            {
                BindToastNotification();
                BindTileNotification();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error notification subscription\n" + e.Message);
            }
        }

        private void BindTileNotification()
        {
            if (_notificationChannel.IsShellTileBound)
                Debug.WriteLine("Already bounded (register) to Tile Notifications");
            else
            {
                Debug.WriteLine("Registering to Tile Notifications");
                // you can register the phone application to receive tile images from remote servers [this is optional]
                if (_tileTrustedServers != null)
                    _notificationChannel.BindToShellTile(_tileTrustedServers);
                else
                    _notificationChannel.BindToShellTile();
            }
        }

        private void BindToastNotification()
        {
            if (_notificationChannel.IsShellToastBound)
                Debug.WriteLine("Already bounded (register) to to Toast notification");
            else
            {
                Debug.WriteLine("Registering to Toast Notifications");
                _notificationChannel.BindToShellToast();
            }
        }

        private void Channel_ChannelUriUpdated(object sender, NotificationChannelUriEventArgs e)
        {
            Debug.WriteLine("Channel opened. Got Uri:\n" + _notificationChannel.ChannelUri);
            Debug.WriteLine("Subscribing to channel events");

            SubscribeToService(AppID);
            SubscribeToNotifications();

            if (OnChannelUriUpdated != null)
                OnChannelUriUpdated(sender, e);
        }

        #endregion
    }
}