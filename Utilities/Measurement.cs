using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Device.Location;

namespace Chetch.Utilities
{
    public static class Measurement
    {
        public const double METER_2_CM = 100;
        public const double MM_2_M = 1000;

        public const double SPEED_OF_SOUND_MPS = 343.0;

        public enum Unit
        {
            NONE,
            CM,
            METER, //standarad
            MM,
            KG, //standard
            GRAM,
            CELSIUS, //standard
            SECOND, //standard
            HOUR,
            MINUTE,
            MICROSECOND,
            MILLISECOND
        }

        //returns number of standard units that are equal to the given unit
        //e.g. 1 cm is given, returned is 0.01 (as in 0.01 meters)
        static private double _asStandardUnit(Unit unit)
        {
            switch (unit)
            {
                case Unit.CM:
                    return 0.01;

                case Unit.MM:
                case Unit.GRAM:
                case Unit.MILLISECOND:
                    return 0.001;
                    
                case Unit.MICROSECOND:
                    return 0.000001;

                case Unit.HOUR:
                    return 3600.0;

                case Unit.MINUTE:
                    return 60.0;
            }
            return 1;
        }

        static public double ConvertUnit(double value, Unit from, Unit to = Unit.NONE)
        {
            return value * _asStandardUnit(from) / _asStandardUnit(to);
        }

        static public double ConvertUnit(Unit from, Unit to = Unit.NONE)
        {
            return ConvertUnit(1, from, to);
        }

        static public double GetDistance(double lat1, double lng1, double lat2, double lng2, bool coordsInDegrees = true)
        {
            if (!coordsInDegrees) // conver to degrees first
            {
                lat1 *=  180.0 / Math.PI;
                lng1 *= 180.0 / Math.PI;
                lat2 *= 180.0 / Math.PI;
                lng2 *= 180.0 / Math.PI;
            }

            GeoCoordinate p1 = new GeoCoordinate(lat1, lng1);
            GeoCoordinate p2 = new GeoCoordinate(lat2, lng2);

            return p1.GetDistanceTo(p2);
        }

        static public double GetInitialBearing(double lat1, double lng1, double lat2, double lng2, bool coordsInDegrees = true)
        {
            if (coordsInDegrees) // conver to radians first
            {
                lat1 /= Math.PI * 180.0;
                lng1 /= Math.PI * 180.0;
                lat2 /= Math.PI * 180.0;
                lng2 /= Math.PI * 180.0;
            }

            //calculate bearing (lat/lon assumed in radians)
            double dLon = (lng2 - lng1);
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

            double brng = Math.Atan2(y, x);
            if (coordsInDegrees)
            {
                return ((brng * 180.0 / Math.PI) + 360) % 360;
            }
            else
            {
                return (brng + 2.0 * Math.PI) % 2.0 * Math.PI;
            }
        }

        static public double GetFinalBearing(double lat1, double lng1, double lat2, double lng2, bool coordsInDegrees = true)
        {
            double bearing = GetInitialBearing(lat2, lng2, lat1, lng1, coordsInDegrees);
            if (coordsInDegrees)
            {
                return (bearing + 180) % 360;
            }
            else
            {
                return (bearing + Math.PI) % 2.0 * Math.PI;
            }

        }

        static public double GetInitiallBearing(GeoCoordinate startPos, GeoCoordinate endPos, bool coordsInDegrees = true)
        {
            return GetInitialBearing(startPos.Latitude, startPos.Longitude, endPos.Latitude, endPos.Longitude, coordsInDegrees);
        }

        static public double GetFinalBearing(GeoCoordinate startPos, GeoCoordinate endPos, bool coordsInDegrees = true)
        {
            return GetFinalBearing(startPos.Latitude, startPos.Longitude, endPos.Latitude, endPos.Longitude, coordsInDegrees);
        }
    }
}
