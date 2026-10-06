using System;
using System.Linq;

namespace GripOpt
{
    /// <summary>Plain (mu/mu_w, lambda)-CMA-ES (Hansen 2016 tutorial), full covariance, Jacobi eigendecomposition.</summary>
    public sealed class Cma
    {
        readonly int n, lambda, mu;
        readonly double[] w; readonly double mueff, cc, cs, c1, cmu, damps, chiN;
        public double[] Mean; public double Sigma;
        double[,] C, B; double[] D, pc, ps;
        readonly Random rng;
        int gen, eigenGen;
        public double[][] Pop;

        public Cma(double[] x0, double sigma, int lambda = 0, int seed = 1)
        {
            n = x0.Length; Mean = (double[])x0.Clone(); Sigma = sigma; rng = new Random(seed);
            this.lambda = lambda > 0 ? lambda : 4 + (int)(3 * Math.Log(n));
            mu = this.lambda / 2;
            w = Enumerable.Range(0, mu).Select(i => Math.Log(mu + .5) - Math.Log(i + 1)).ToArray();
            double sw = w.Sum(); for (int i = 0; i < mu; i++) w[i] /= sw;
            mueff = 1 / w.Sum(x => x * x);
            cc = (4 + mueff / n) / (n + 4 + 2 * mueff / n);
            cs = (mueff + 2) / (n + mueff + 5);
            c1 = 2 / ((n + 1.3) * (n + 1.3) + mueff);
            cmu = Math.Min(1 - c1, 2 * (mueff - 2 + 1 / mueff) / ((n + 2) * (n + 2) + mueff));
            damps = 1 + 2 * Math.Max(0, Math.Sqrt((mueff - 1) / (n + 1)) - 1) + cs;
            chiN = Math.Sqrt(n) * (1 - 1.0 / (4 * n) + 1.0 / (21 * n * n));
            C = new double[n, n]; B = new double[n, n]; D = new double[n]; pc = new double[n]; ps = new double[n];
            for (int i = 0; i < n; i++) { C[i, i] = 1; B[i, i] = 1; D[i] = 1; }
        }

        public double[][] Ask()
        {
            Pop = new double[lambda][];
            for (int k = 0; k < lambda; k++)
            {
                var z = new double[n]; for (int i = 0; i < n; i++) z[i] = D[i] * Gauss();
                var x = new double[n];
                for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += B[i, j] * z[j]; x[i] = Mean[i] + Sigma * s; }
                Pop[k] = x;
            }
            return Pop;
        }

        public void Tell(double[] cost)
        {
            var order = Enumerable.Range(0, lambda).OrderBy(k => cost[k]).ToArray();
            var old = (double[])Mean.Clone();
            for (int i = 0; i < n; i++) { double s = 0; for (int k = 0; k < mu; k++) s += w[k] * Pop[order[k]][i]; Mean[i] = s; }
            var y = new double[n]; for (int i = 0; i < n; i++) y[i] = (Mean[i] - old[i]) / Sigma;
            // C^-1/2 y = B D^-1 B^T y
            var t = new double[n];
            for (int j = 0; j < n; j++) { double s = 0; for (int i = 0; i < n; i++) s += B[i, j] * y[i]; t[j] = s / D[j]; }
            var cy = new double[n];
            for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += B[i, j] * t[j]; cy[i] = s; }
            for (int i = 0; i < n; i++) ps[i] = (1 - cs) * ps[i] + Math.Sqrt(cs * (2 - cs) * mueff) * cy[i];
            double psn = Math.Sqrt(ps.Sum(x => x * x));
            gen++;
            bool hsig = psn / Math.Sqrt(1 - Math.Pow(1 - cs, 2 * gen)) / chiN < 1.4 + 2.0 / (n + 1);
            for (int i = 0; i < n; i++) pc[i] = (1 - cc) * pc[i] + (hsig ? Math.Sqrt(cc * (2 - cc) * mueff) : 0) * y[i];
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= i; j++)
                {
                    double r = 0;
                    for (int k = 0; k < mu; k++)
                    {
                        var x = Pop[order[k]];
                        r += w[k] * (x[i] - old[i]) / Sigma * (x[j] - old[j]) / Sigma;
                    }
                    double c = (1 - c1 - cmu) * C[i, j] + c1 * (pc[i] * pc[j] + (hsig ? 0 : cc * (2 - cc) * C[i, j])) + cmu * r;
                    C[i, j] = C[j, i] = c;
                }
            Sigma *= Math.Exp(cs / damps * (psn / chiN - 1));
            if (gen - eigenGen > lambda / (c1 + cmu) / n / 10) { eigenGen = gen; Eigen(); }
        }

        void Eigen()
        {
            var a = (double[,])C.Clone(); var v = new double[n, n];
            for (int i = 0; i < n; i++) v[i, i] = 1;
            for (int sweep = 0; sweep < 60; sweep++)
            {
                double off = 0;
                for (int p = 0; p < n; p++) for (int q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
                if (off < 1e-22) break;
                for (int p = 0; p < n; p++)
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double tt = Math.Sign(th) / (Math.Abs(th) + Math.Sqrt(th * th + 1)); if (th == 0) tt = 1;
                        double c = 1 / Math.Sqrt(tt * tt + 1), s = tt * c;
                        for (int k = 0; k < n; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                        }
                    }
            }
            for (int i = 0; i < n; i++) { D[i] = Math.Sqrt(Math.Max(1e-20, a[i, i])); for (int k = 0; k < n; k++) B[k, i] = v[k, i]; }
        }

        double Gauss()
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
