using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class Note
    {
        public String Id { get; set; }
        public Interval Interval { get; set; }
        public String Text { get; set; }
        public bool IsFocused { get; set; }

        public override int GetHashCode()
        {
            return (this.Id != null ? this.Id.GetHashCode() : 0);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (obj.GetType() != this.GetType())
            {
                return false;
            }
            return Equals((Note) obj);
        }

        private bool Equals(Note other)
        {
            return string.Equals(this.Id, other.Id);
        }
    }
}
