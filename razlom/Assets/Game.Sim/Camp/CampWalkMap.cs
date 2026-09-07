namespace Game.Sim
{
    // Immutable walkability snapshot. Unity bakes it once; all movement queries
    // (including forced ability motion) then use only fixed point arithmetic.
    public sealed class CampWalkMap
    {
        readonly bool[] _cells;
        readonly int _width, _height;
        readonly FixVec2 _origin;
        public readonly Fix64 CellSize;
        public CampWalkMap(FixVec2 origin, Fix64 cellSize, int width, int height, bool[] cells)
        {
            _origin = origin; CellSize = cellSize; _width = width; _height = height;
            _cells = (bool[])cells.Clone();
        }
        public bool Contains(FixVec2 point)
        {
            FixVec2 local = point - _origin;
            if (local.X < Fix64.Zero || local.Y < Fix64.Zero) return false;
            int x = (local.X / CellSize).ToInt(), y = (local.Y / CellSize).ToInt();
            return x < _width && y < _height && _cells[y * _width + x];
        }
        public bool CanTravel(FixVec2 from, FixVec2 to)
        {
            FixVec2 delta = to - from;
            int steps = System.Math.Max(1, (delta.Length / (CellSize / Fix64.FromInt(2))).ToInt() + 1);
            for (int i = 1; i <= steps; i++)
                if (!Contains(from + delta * Fix64.Ratio(i, steps))) return false;
            return true;
        }

        FixVec2 Center(int index) => _origin + new FixVec2(
            (Fix64.FromInt(index % _width) + Fix64.Half) * CellSize,
            (Fix64.FromInt(index / _width) + Fix64.Half) * CellSize);

        int Nearest(FixVec2 point)
        {
            int best=-1;Fix64 distance=Fix64.FromInt(100000);
            for(int i=0;i<_cells.Length;i++)
            {
                if(!_cells[i])continue;
                Fix64 d=(Center(i)-point).LengthSq;
                if(d<distance){distance=d;best=i;}
            }
            return best;
        }

        // Cold interaction query; ordinary RMB movement uses the combat motor.
        // Route on the same snapshot as collision, avoiding NavMesh edge paths
        // that disappear when their cells are quantized for the simulation.
        public FixVec2[] FindPath(FixVec2 from, FixVec2 to)
        {
            int start=Nearest(from),goal=Nearest(to);
            if(start<0||goal<0)return System.Array.Empty<FixVec2>();
            var previous=new int[_cells.Length];
            for(int i=0;i<previous.Length;i++)previous[i]=-1;
            var queue=new int[_cells.Length];int head=0,tail=0;
            queue[tail++]=start;previous[start]=start;
            while(head<tail&&previous[goal]<0)
            {
                int at=queue[head++],x=at%_width,y=at/_width;
                for(int axis=0;axis<4;axis++)
                {
                    int nx=x+(axis==0?1:axis==1?-1:0),ny=y+(axis==2?1:axis==3?-1:0);
                    if(nx<0||nx>=_width||ny<0||ny>=_height)continue;
                    int next=ny*_width+nx;
                    if(!_cells[next]||previous[next]>=0)continue;
                    previous[next]=at;queue[tail++]=next;
                }
            }
            if(previous[goal]<0)return System.Array.Empty<FixVec2>();
            var reverse=new System.Collections.Generic.List<FixVec2>();
            for(int at=goal;at!=start;at=previous[at])reverse.Add(Center(at));
            reverse.Add(from);reverse.Reverse();
            var path=new System.Collections.Generic.List<FixVec2>{from};
            int corner=0;
            while(corner<reverse.Count-1)
            {
                int next=reverse.Count-1;
                while(next>corner+1&&!CanTravel(reverse[corner],reverse[next]))next--;
                path.Add(reverse[next]);corner=next;
            }
            return path.ToArray();
        }
    }
}
