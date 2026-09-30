namespace Game.Sim
{
    // Неизменяемая карта ходьбы. Unity строит её один раз; движение, в том числе
    // принудительное от способностей, затем использует только фиксированную арифметику.
    public sealed class CampWalkMap
    {
        readonly bool[] _cells;
        readonly int _width, _height;
        readonly FixVec2 _origin;
        readonly int[] _previous, _seen, _queue;
        readonly int[] _components;
        int _searchToken;
        public readonly Fix64 CellSize;
        public int ComponentCount { get; private set; }
        public int WalkableCellCount { get; private set; }
        public CampWalkMap(FixVec2 origin, Fix64 cellSize, int width, int height, bool[] cells)
        {
            _origin = origin; CellSize = cellSize; _width = width; _height = height;
            _cells = (bool[])cells.Clone();
            _previous = new int[cells.Length];
            _seen = new int[cells.Length];
            _queue = new int[cells.Length];
            _components = new int[cells.Length];
            BuildComponents();
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

        void BuildComponents()
        {
            int component = 0;
            for (int first = 0; first < _cells.Length; first++)
            {
                if (!_cells[first] || _components[first] != 0) continue;
                component++; int head = 0, tail = 0;
                _queue[tail++] = first; _components[first] = component;
                while (head < tail)
                {
                    int at = _queue[head++], x = at % _width, y = at / _width;
                    WalkableCellCount++;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        int nx = x + (axis == 0 ? 1 : axis == 1 ? -1 : 0);
                        int ny = y + (axis == 2 ? 1 : axis == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;
                        int next = ny * _width + nx;
                        if (!_cells[next] || _components[next] != 0) continue;
                        _components[next] = component; _queue[tail++] = next;
                    }
                }
            }
            ComponentCount = component;
        }

        int Nearest(FixVec2 point, int component = 0)
        {
            FixVec2 local=point-_origin;
            int px=(local.X/CellSize).ToInt(),py=(local.Y/CellSize).ToInt();
            if(px>=0&&py>=0&&px<_width&&py<_height&&_cells[py*_width+px]
                && (component == 0 || _components[py*_width+px] == component))return py*_width+px;
            int cx=System.Math.Max(0,System.Math.Min(_width-1,px));
            int cy=System.Math.Max(0,System.Math.Min(_height-1,py));
            int best=-1;Fix64 distance=Fix64.FromInt(100000);
            // Большинство закрытых кликов рядом с препятствием: сначала ищем локально.
            // Обход всех 300 тысяч клеток на каждый клик заметно тормозил лагерь.
            for(int radius=0;radius<=24;radius++)
            {
                int minX=System.Math.Max(0,cx-radius),maxX=System.Math.Min(_width-1,cx+radius);
                int minY=System.Math.Max(0,cy-radius),maxY=System.Math.Min(_height-1,cy+radius);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    if(radius>0&&x>minX&&x<maxX&&y>minY&&y<maxY)continue;
                    int i=y*_width+x;if(!_cells[i] || component != 0 && _components[i] != component)continue;
                    Fix64 d=(Center(i)-point).LengthSq;
                    if(best<0 || d<distance){distance=d;best=i;}
                }
                if(best>=0&&Fix64.FromInt(radius)*CellSize>Fix64.Sqrt(distance)+CellSize)return best;
            }
            for(int i=0;i<_cells.Length;i++)if(_cells[i] && (component == 0 || _components[i] == component))
            {
                Fix64 d=(Center(i)-point).LengthSq;
                if(best<0 || d<distance){distance=d;best=i;}
            }
            return best;
        }

        public bool TryNearestReachable(FixVec2 from, FixVec2 target, out FixVec2 reachable)
        {
            reachable = from;
            int start = Nearest(from);
            if (start < 0) return false;
            int goal = Nearest(target, _components[start]);
            if (goal < 0) return false;
            reachable = Center(goal); return true;
        }

        // ПКМ и подходы к сервисам используют ту же карту, что столкновения движения.
        // Так пути по краям NavMesh не исчезают при округлении в клетки симуляции.
        public FixVec2[] FindPath(FixVec2 from, FixVec2 to)
        {
            int start=Nearest(from);
            int goal=start>=0?Nearest(to,_components[start]):-1;
            if(start<0||goal<0)return System.Array.Empty<FixVec2>();
            FixVec2 goalPoint=Center(goal);
            if(Contains(from)&&CanTravel(from,goalPoint))return new[]{from,goalPoint};
            if(_searchToken==int.MaxValue){System.Array.Clear(_seen,0,_seen.Length);_searchToken=0;}
            int token=++_searchToken,head=0,tail=0;
            _queue[tail++]=start;_seen[start]=token;_previous[start]=start;
            while(head<tail&&_seen[goal]!=token)
            {
                int at=_queue[head++],x=at%_width,y=at/_width;
                for(int axis=0;axis<4;axis++)
                {
                    int nx=x+(axis==0?1:axis==1?-1:0),ny=y+(axis==2?1:axis==3?-1:0);
                    if(nx<0||nx>=_width||ny<0||ny>=_height)continue;
                    int next=ny*_width+nx;
                    if(!_cells[next]||_seen[next]==token)continue;
                    _seen[next]=token;_previous[next]=at;_queue[tail++]=next;
                }
            }
            if(_seen[goal]!=token)return System.Array.Empty<FixVec2>();
            var reverse=new System.Collections.Generic.List<FixVec2>();
            for(int at=goal;at!=start;at=_previous[at])reverse.Add(Center(at));
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

        /// <summary>Только для лагеря: скольжение вдоль местной границы вместо шагов по осям мира.</summary>
        public FixVec2 Slide(FixVec2 from, FixVec2 delta)
        {
            if (delta.LengthSq.Raw == 0 || !Contains(from)) return from;
            if (CanTravel(from, from + delta)) return from + delta;
            FixVec2 contact = Furthest(from, delta);
            FixVec2 remaining = from + delta - contact;
            FixVec2 normal = BoundaryNormal(contact);
            if (normal.LengthSq.Raw > 0)
            {
                Fix64 dot = remaining.X * normal.X + remaining.Y * normal.Y;
                FixVec2 tangent = remaining - normal * (dot / normal.LengthSq);
                // Малый наружный отступ не даёт ступенчатым клеткам снова поймать касательную.
                FixVec2 clearance = normal / Fix64.Sqrt(normal.LengthSq) * (CellSize / Fix64.FromInt(16));
                FixVec2 candidate = contact + tangent + clearance;
                candidate = from + (candidate - from).ClampLength(delta.Length);
                if (CanTravel(from, candidate)) return candidate;
                FixVec2 along = Furthest(contact, tangent);
                if ((along - contact).LengthSq.Raw > 0) return along;
                // Край растра ступенчатый даже у диагональной границы. Отклоняем
                // касательную наружу до свободного отрезка, иначе край клетки
                // продолжает цеплять движение бесконечно.
                if (tangent.LengthSq.Raw > 0)
                {
                    FixVec2 direction = tangent.Normalized(), outward = normal.Normalized();
                    for (int turn = 1; turn <= 8; turn++)
                    {
                        FixVec2 bent = (direction + outward * Fix64.Ratio(turn, 4)).Normalized() * remaining.Length;
                        candidate = contact + bent;
                        candidate = from + (candidate - from).ClampLength(delta.Length);
                        if (CanTravel(from, candidate)) return candidate;
                    }
                }
            }
            if ((contact - from).LengthSq > CellSize * CellSize / Fix64.FromInt(256)) return contact;
            // У тесного вогнутого угла может не быть одной устойчивой нормали.
            bool xFirst = Fix64.Abs(delta.X) >= Fix64.Abs(delta.Y);
            FixVec2 first = xFirst ? new FixVec2(delta.X, Fix64.Zero) : new FixVec2(Fix64.Zero, delta.Y);
            if (CanTravel(from, from + first)) return from + first;
            FixVec2 second = delta - first;
            return CanTravel(from, from + second) ? from + second : from;
        }

        FixVec2 Furthest(FixVec2 from, FixVec2 delta)
        {
            Fix64 low = Fix64.Zero, high = Fix64.One;
            for (int i = 0; i < 10; i++)
            {
                Fix64 middle = (low + high) * Fix64.Half;
                if (CanTravel(from, from + delta * middle)) low = middle; else high = middle;
            }
            return from + delta * low;
        }

        FixVec2 BoundaryNormal(FixVec2 at)
        {
            FixVec2 local = at - _origin;
            int cx = (local.X / CellSize).ToInt(), cy = (local.Y / CellSize).ToInt();
            Fix64 reach = CellSize * Fix64.FromInt(4), reachSq = reach * reach;
            FixVec2 sum = FixVec2.Zero;
            for (int y = cy - 4; y <= cy + 4; y++)
            for (int x = cx - 4; x <= cx + 4; x++)
            {
                if (x >= 0 && y >= 0 && x < _width && y < _height && _cells[y * _width + x]) continue;
                FixVec2 center = _origin + new FixVec2((Fix64.FromInt(x) + Fix64.Half) * CellSize,
                    (Fix64.FromInt(y) + Fix64.Half) * CellSize);
                FixVec2 away = at - center; Fix64 distanceSq = away.LengthSq;
                if (distanceSq.Raw == 0 || distanceSq >= reachSq) continue;
                sum += away * ((reachSq - distanceSq) / Fix64.Max(distanceSq, CellSize * CellSize / Fix64.FromInt(16)));
            }
            return sum;
        }
    }
}
