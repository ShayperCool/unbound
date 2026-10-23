using Unbound.Box2D.Dynamics;
using Unbound.Box2D.Dynamics.Contacts;

namespace Unbound.Box2D.Common
{
    internal static class EnumExtensions
    {
        public static bool IsSet(this BodyFlags self, BodyFlags flag) => (self & flag) == flag;

        public static bool IsSet(this DrawFlag self, DrawFlag flag) => (self & flag) == flag;

        public static bool IsSet(this Contact.ContactFlag self, Contact.ContactFlag flag) => (self & flag) == flag;
    }
}