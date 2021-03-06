using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections;
using System.Reflection;
using System.Linq;
using System;

using Android.Gms.Maps.Model;
using Android.Gms.Maps;
using Android.OS;

using Java.Lang;

using Xamarin.Forms.Platform.Android;
using Xamarin.Forms.Maps;
using Xamarin.Forms;

using Math = System.Math;

namespace XamarinForms.Maps.Android.TemporaryPatch
{
    public static class MapRendererConstants
    {
        public static readonly PropertyInfo LastMoveToRegionProperty = typeof(Map).GetProperty("LastMoveToRegion", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly PropertyInfo VisibleRegionProperty = typeof(Map).GetProperty("VisibleRegion");
        public static readonly PropertyInfo PinIdProperty = typeof(Pin).GetProperty("Id", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly MethodInfo PinSendTapMethod = typeof(Pin).GetMethod("SendTap", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly object[] EmptyArgumentArray = new object[0];
        public static Bundle Bundle { get; set; }
    }

    public class MapRenderer : MapRenderer<Map> { }

    public class MapRenderer<TMap> : ViewRenderer<TMap, MapView>, 
        GoogleMap.IOnCameraIdleListener, 
        GoogleMap.IOnCameraMoveListener, 
        GoogleMap.IOnCameraMoveStartedListener, 
        GoogleMap.IOnCameraMoveCanceledListener, 
        IOnMapReadyCallback
        where TMap : Map
    {
        const string MoveMessageName = "MapMoveToRegion";

        bool _disposed;

        bool _init;

        List<Marker> _markers;

        public MapRenderer()
        {
            AutoPackage = false;
        }

        protected Map Map => Element;

        protected GoogleMap NativeMap;

        public void OnCameraChange(CameraPosition pos)
        {
            UpdateVisibleRegion(pos.Target);
        }

        public override SizeRequest GetDesiredSize(int widthConstraint, int heightConstraint)
        {
            return new SizeRequest(new Size(Context.ToPixels(40), Context.ToPixels(40)));
        }

        protected override MapView CreateNativeControl()
        {
            return new MapView(Context);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing)
            {
                if (Element != null)
                {
                    MessagingCenter.Unsubscribe<Map, MapSpan>(this, MoveMessageName);
                    ((ObservableCollection<Pin>)Element.Pins).CollectionChanged -= OnCollectionChanged;
                }

                if (NativeMap != null)
                {
                    NativeMap.MyLocationEnabled = false;
                    UnhookNativeMapEvents();
                    NativeMap.Dispose();
                }

                Control?.OnDestroy();
            }

            base.Dispose(disposing);
        }

        protected override void OnElementChanged(ElementChangedEventArgs<TMap> e)
        {
            base.OnElementChanged(e);

            MapView oldMapView = Control;

            MapView mapView = CreateNativeControl();
            mapView.OnCreate(MapRendererConstants.Bundle);
            mapView.OnResume();
            SetNativeControl(mapView);

            if (e.OldElement != null)
            {
                Map oldMapModel = e.OldElement;
                ((ObservableCollection<Pin>)oldMapModel.Pins).CollectionChanged -= OnCollectionChanged;
                MessagingCenter.Unsubscribe<Map, MapSpan>(this, MoveMessageName);
                if (NativeMap != null)
                {
                    UnhookNativeMapEvents();
                }

                oldMapView.Dispose();
            }

            _init = true;
            Control.GetMapAsync(this);
            MessagingCenter.Subscribe<Map, MapSpan>(this, MoveMessageName, OnMoveToRegionMessage, Map);

            var incc = Map.Pins as INotifyCollectionChanged;
            if (incc != null)
            {
                incc.CollectionChanged += OnCollectionChanged;
            }
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if (e.PropertyName == Map.MapTypeProperty.PropertyName)
            {
                SetMapType();
                return;
            }

            GoogleMap gmap = NativeMap;
            if (gmap == null)
            {
                return;
            }

            if (e.PropertyName == Map.IsShowingUserProperty.PropertyName)
            {
                gmap.MyLocationEnabled = gmap.UiSettings.MyLocationButtonEnabled = Map.IsShowingUser;
            }
            else if (e.PropertyName == Map.HasScrollEnabledProperty.PropertyName)
            {
                gmap.UiSettings.ScrollGesturesEnabled = Map.HasScrollEnabled;
            }
            else if (e.PropertyName == Map.HasZoomEnabledProperty.PropertyName)
            {
                gmap.UiSettings.ZoomControlsEnabled = Map.HasZoomEnabled;
                gmap.UiSettings.ZoomGesturesEnabled = Map.HasZoomEnabled;
            }
        }

        protected override void OnLayout(bool changed, int l, int t, int r, int b)
        {
            base.OnLayout(changed, l, t, r, b);
            if (_init || !changed) return;
            UpdateVisibleRegion(NativeMap.CameraPosition.Target);
            MoveToLastRegion();
        }

        private void MoveToLastRegion()
        {
            var mapSpan = MapRendererConstants.LastMoveToRegionProperty.GetValue(Element) as MapSpan;
            if (mapSpan != null) MoveToRegion(mapSpan, false);
        }

        void AddPins(IList pins)
        {
            GoogleMap map = NativeMap;
            if (map == null)
            {
                return;
            }

            if (_markers == null)
            {
                _markers = new List<Marker>();
            }

            _markers.AddRange(pins.Cast<Pin>().Select(p =>
            {
                Pin pin = p;
                var opts = new MarkerOptions();
                opts.SetPosition(new LatLng(pin.Position.Latitude, pin.Position.Longitude));
                opts.SetTitle(pin.Label);
                opts.SetSnippet(pin.Address);
                var marker = map.AddMarker(opts);

                // associate pin with marker for later lookup in event handlers
                MapRendererConstants.PinIdProperty.SetValue(pin, marker.Id);
                return marker;
            }));
        }

        void MapOnMarkerClick(object sender, GoogleMap.InfoWindowClickEventArgs eventArgs)
        {
            // clicked marker
            var marker = eventArgs.Marker;

            // lookup pin
            Pin targetPin = null;
            for (var i = 0; i < Map.Pins.Count; i++)
            {
                Pin pin = Map.Pins[i];
                if ((string)MapRendererConstants.PinIdProperty.GetValue(pin) != marker.Id)
                {
                    continue;
                }

                targetPin = pin;
                break;
            }

            // only consider event handled if a handler is present. 
            // Else allow default behavior of displaying an info window.
            if (targetPin != null) MapRendererConstants.PinSendTapMethod.Invoke(targetPin, MapRendererConstants.EmptyArgumentArray);
        }

        void MoveToRegion(MapSpan span, bool animate)
        {
            GoogleMap map = NativeMap;
            if (map == null)
            {
                return;
            }

            span = span.ClampLatitude(85, -85);
            var ne = new LatLng(span.Center.Latitude + span.LatitudeDegrees / 2,
                span.Center.Longitude + span.LongitudeDegrees / 2);
            var sw = new LatLng(span.Center.Latitude - span.LatitudeDegrees / 2,
                span.Center.Longitude - span.LongitudeDegrees / 2);
            CameraUpdate update = CameraUpdateFactory.NewLatLngBounds(new LatLngBounds(sw, ne), 0);

            try
            {
                if (animate)
                {
                    map.AnimateCamera(update);
                }
                else
                {
                    map.MoveCamera(update);
                }
            }
            catch (IllegalStateException exc)
            {
                System.Diagnostics.Debug.WriteLine("MoveToRegion exception: " + exc);
            }
        }

        void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
        {
            switch (notifyCollectionChangedEventArgs.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddPins(notifyCollectionChangedEventArgs.NewItems);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemovePins(notifyCollectionChangedEventArgs.OldItems);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    RemovePins(notifyCollectionChangedEventArgs.OldItems);
                    AddPins(notifyCollectionChangedEventArgs.NewItems);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    _markers?.ForEach(m => m.Remove());
                    _markers = null;
                    AddPins((IList)Element.Pins);
                    break;
                case NotifyCollectionChangedAction.Move:
                    //do nothing
                    break;
            }
        }

        void OnMoveToRegionMessage(Map s, MapSpan a)
        {
            MoveToRegion(a, true);
        }

        void RemovePins(IList pins)
        {
            GoogleMap map = NativeMap;
            if (map == null)
            {
                return;
            }
            if (_markers == null)
            {
                return;
            }

            foreach (Pin p in pins)
            {
                var marker = _markers.FirstOrDefault(m => (object)m.Id == MapRendererConstants.PinIdProperty.GetValue(p));
                if (marker == null)
                {
                    continue;
                }
                marker.Remove();
                _markers.Remove(marker);
            }
        }

        void SetMapType()
        {
            GoogleMap map = NativeMap;
            if (map == null)
            {
                return;
            }

            switch (Map.MapType)
            {
                case MapType.Street:
                    map.MapType = GoogleMap.MapTypeNormal;
                    break;
                case MapType.Satellite:
                    map.MapType = GoogleMap.MapTypeSatellite;
                    break;
                case MapType.Hybrid:
                    map.MapType = GoogleMap.MapTypeHybrid;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void UpdateVisibleRegion(LatLng pos)
        {
            GoogleMap map = NativeMap;
            if (map == null)
            {
                return;
            }
            Projection projection = map.Projection;
            int width = Control.Width;
            int height = Control.Height;
            LatLng ul = projection.FromScreenLocation(new global::Android.Graphics.Point(0, 0));
            LatLng ur = projection.FromScreenLocation(new global::Android.Graphics.Point(width, 0));
            LatLng ll = projection.FromScreenLocation(new global::Android.Graphics.Point(0, height));
            LatLng lr = projection.FromScreenLocation(new global::Android.Graphics.Point(width, height));
            double dlat = Math.Max(Math.Abs(ul.Latitude - lr.Latitude), Math.Abs(ur.Latitude - ll.Latitude));
            double dlong = Math.Max(Math.Abs(ul.Longitude - lr.Longitude), Math.Abs(ur.Longitude - ll.Longitude));
            MapRendererConstants.VisibleRegionProperty.SetValue(Element, new MapSpan(new Position(pos.Latitude, pos.Longitude), dlat, dlong));
        }

        public virtual void OnMapReady(GoogleMap googleMap)
        {
            NativeMap = googleMap;
            if (NativeMap != null)
            {
                HookUpNativeMapEvents();
                NativeMap.UiSettings.ZoomControlsEnabled = Map.HasZoomEnabled;
                NativeMap.UiSettings.ZoomGesturesEnabled = Map.HasZoomEnabled;
                NativeMap.UiSettings.ScrollGesturesEnabled = Map.HasScrollEnabled;
                NativeMap.MyLocationEnabled = NativeMap.UiSettings.MyLocationButtonEnabled = Map.IsShowingUser;
                SetMapType();
                MoveToLastRegion();
                OnCollectionChanged(Element.Pins, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                _init = false;
            }
        }

        private void HookUpNativeMapEvents()
        {
            NativeMap.SetOnCameraIdleListener(this);
            NativeMap.SetOnCameraMoveListener(this);
            NativeMap.SetOnCameraMoveStartedListener(this);
            NativeMap.SetOnCameraMoveCanceledListener(this);
            NativeMap.InfoWindowClick += MapOnMarkerClick;
        }

        private void UnhookNativeMapEvents()
        {
            NativeMap.SetOnCameraIdleListener(null);
            NativeMap.SetOnCameraMoveListener(null);
            NativeMap.SetOnCameraMoveStartedListener(null);
            NativeMap.SetOnCameraMoveCanceledListener(null);
            NativeMap.InfoWindowClick -= MapOnMarkerClick;
        }

        public virtual void OnCameraIdle() => UpdateVisibleRegion(NativeMap.CameraPosition.Target);
        public virtual void OnCameraMove() => UpdateVisibleRegion(NativeMap.CameraPosition.Target);
        public virtual void OnCameraMoveStarted(int reason) => UpdateVisibleRegion(NativeMap.CameraPosition.Target);
        public virtual void OnCameraMoveCanceled() => UpdateVisibleRegion(NativeMap.CameraPosition.Target);
    }
}