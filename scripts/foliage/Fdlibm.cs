using System;

namespace DillControl.Foliage;

/// <summary>
/// sin, cos, tan, atan2 and exp as fdlibm computes them (the port follows
/// V8's src/base/ieee754.cc), so a seed grows the same vine on every machine
/// and runtime. .NET's own differ in the last bit on 2-18 % of inputs between
/// versions, and a vine's voxels sit on floor() thresholds. Only the range the
/// generator uses is ported (|x| under 2^19 pi/2); past it, .NET's answer.
/// </summary>
public static class Fdlibm
{
    private static int Hi(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);
    private static uint Lo(double x) => (uint)BitConverter.DoubleToInt64Bits(x);
    private static double Words(int hi, uint lo) => BitConverter.Int64BitsToDouble(((long)hi << 32) | lo);
    private static double WithLo(double x, uint lo) => Words(Hi(x), lo);

    private static double KernelSin(double x, double y, int iy)
    {
        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03,
            S3 = -1.98412698298579493134e-04, S4 = 2.75573137070700676789e-06,
            S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0)
            return x;
        double z = x * x;
        double v = z * x;
        double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
        if (iy == 0)
            return x + v * (S1 + z * r);
        return x - ((z * (0.5 * y - v * r) - y) - v * S1);
    }

    private static double KernelCos(double x, double y)
    {
        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03,
            C3 = 2.48015872894767294178e-05, C4 = -2.75573143513906633035e-07,
            C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0)
            return 1.0;
        double z = x * x;
        double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        if (ix < 0x3fd33333)
            return 1.0 - (0.5 * z - (z * r - x * y));
        double qx = ix > 0x3fe90000 ? 0.28125 : Words(ix - 0x00200000, 0);
        double hz = 0.5 * z - qx;
        double a = 1.0 - qx;
        return a - (hz - (z * r - x * y));
    }

    private static readonly double[] T =
    {
        3.33333333333334091986e-01, 1.33333333333201242699e-01, 5.39682539762260521377e-02,
        2.18694882948595424599e-02, 8.86323982359930005737e-03, 3.59207910759131235356e-03,
        1.45620945432529025516e-03, 5.88041240820264096874e-04, 2.46463134818469906812e-04,
        7.81794442939557092300e-05, 7.14072491382608190305e-05, -1.85586374855275456654e-05,
        2.59073051863633712884e-05,
    };

    private static double KernelTan(double x, double y, int iy)
    {
        const double pio4 = 7.85398163397448278999e-01, pio4lo = 3.06161699786838301793e-17;
        double z, r, v, w, s;
        int hx = Hi(x);
        int ix = hx & 0x7fffffff;
        if (ix < 0x3e300000 && (int)x == 0)
        {
            if (((ix | (int)Lo(x)) | (iy + 1)) == 0)
                return 1.0 / Math.Abs(x);
            if (iy == 1)
                return x;
            z = w = x + y;
            z = WithLo(z, 0);
            v = y - (z - x);
            double a0 = -1.0 / w;
            double t0 = WithLo(a0, 0);
            s = 1.0 + t0 * z;
            return t0 + a0 * (s + t0 * v);
        }
        if (ix >= 0x3fe59428)
        {
            if (hx < 0)
            {
                x = -x;
                y = -y;
            }
            z = pio4 - x;
            w = pio4lo - y;
            x = z + w;
            y = 0.0;
        }
        z = x * x;
        w = z * z;
        r = T[1] + w * (T[3] + w * (T[5] + w * (T[7] + w * (T[9] + w * T[11]))));
        v = z * (T[2] + w * (T[4] + w * (T[6] + w * (T[8] + w * (T[10] + w * T[12])))));
        s = z * x;
        r = y + z * (s * (r + v) + y);
        r += T[0] * s;
        w = x + r;
        if (ix >= 0x3fe59428)
        {
            v = iy;
            return (1 - ((hx >> 30) & 2)) * (v - 2.0 * (x - (w * w / (w + v) - r)));
        }
        if (iy == 1)
            return w;
        z = WithLo(w, 0);
        v = r - (z - x);
        double a = -1.0 / w;
        double t = WithLo(a, 0);
        s = 1.0 + t * z;
        return t + a * (s + t * v);
    }

    private static readonly int[] Npio2Hw =
    {
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB,
        0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB,
        0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
        0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB,
    };

    /// <summary>x reduced by n pi/2 into y0 + y1; false past the medium range.</summary>
    private static bool RemPio2(double x, out int n, out double y0, out double y1)
    {
        const double invpio2 = 6.36619772367581382433e-01,
            pio2_1 = 1.57079632673412561417e+00, pio2_1t = 6.07710050650619224932e-11,
            pio2_2 = 6.07710050630396597660e-11, pio2_2t = 2.02226624879595063154e-21,
            pio2_3 = 2.02226624871116645580e-21, pio2_3t = 8.47842766036889956997e-32;
        int hx = Hi(x);
        int ix = hx & 0x7fffffff;
        double z;
        if (ix < 0x4002d97c)
        {
            // |x| < 3pi/4: n is +-1.
            if (hx > 0)
            {
                z = x - pio2_1;
                if (ix != 0x3ff921fb)
                {
                    y0 = z - pio2_1t;
                    y1 = (z - y0) - pio2_1t;
                }
                else
                {
                    z -= pio2_2;
                    y0 = z - pio2_2t;
                    y1 = (z - y0) - pio2_2t;
                }
                n = 1;
                return true;
            }
            z = x + pio2_1;
            if (ix != 0x3ff921fb)
            {
                y0 = z + pio2_1t;
                y1 = (z - y0) + pio2_1t;
            }
            else
            {
                z += pio2_2;
                y0 = z + pio2_2t;
                y1 = (z - y0) + pio2_2t;
            }
            n = -1;
            return true;
        }
        if (ix > 0x413921fb)
        {
            n = 0;
            y0 = y1 = 0;
            return false;
        }
        double t = Math.Abs(x);
        n = (int)(t * invpio2 + 0.5);
        double fn = n;
        double r = t - fn * pio2_1;
        double w = fn * pio2_1t;
        if (n < 32 && ix != Npio2Hw[n - 1])
        {
            y0 = r - w;
        }
        else
        {
            int j = ix >> 20;
            y0 = r - w;
            int i = j - ((Hi(y0) >> 20) & 0x7ff);
            if (i > 16)
            {
                t = r;
                w = fn * pio2_2;
                r = t - w;
                w = fn * pio2_2t - ((t - r) - w);
                y0 = r - w;
                i = j - ((Hi(y0) >> 20) & 0x7ff);
                if (i > 49)
                {
                    t = r;
                    w = fn * pio2_3;
                    r = t - w;
                    w = fn * pio2_3t - ((t - r) - w);
                    y0 = r - w;
                }
            }
        }
        y1 = (r - y0) - w;
        if (hx < 0)
        {
            y0 = -y0;
            y1 = -y1;
            n = -n;
        }
        return true;
    }

    public static double Sin(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb)
            return KernelSin(x, 0, 0);
        if (ix >= 0x7ff00000)
            return x - x;
        if (!RemPio2(x, out int n, out double y0, out double y1))
            return Math.Sin(x);
        return (n & 3) switch
        {
            0 => KernelSin(y0, y1, 1),
            1 => KernelCos(y0, y1),
            2 => -KernelSin(y0, y1, 1),
            _ => -KernelCos(y0, y1),
        };
    }

    public static double Cos(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb)
            return KernelCos(x, 0);
        if (ix >= 0x7ff00000)
            return x - x;
        if (!RemPio2(x, out int n, out double y0, out double y1))
            return Math.Cos(x);
        return (n & 3) switch
        {
            0 => KernelCos(y0, y1),
            1 => -KernelSin(y0, y1, 1),
            2 => -KernelCos(y0, y1),
            _ => KernelSin(y0, y1, 1),
        };
    }

    public static double Tan(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb)
            return KernelTan(x, 0, 1);
        if (ix >= 0x7ff00000)
            return x - x;
        if (!RemPio2(x, out int n, out double y0, out double y1))
            return Math.Tan(x);
        return KernelTan(y0, y1, 1 - ((n & 1) << 1));
    }

    private static readonly double[] AtanHi =
        { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
    private static readonly double[] AtanLo =
        { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
    private static readonly double[] AT =
    {
        3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01,
        -1.11111104054623557880e-01, 9.09088713343650656196e-02, -7.69187620504482999495e-02,
        6.66107313738753120669e-02, -5.83357013379057348645e-02, 4.97687799461593236017e-02,
        -3.65315727442169155270e-02, 1.62858201153657823623e-02,
    };

    public static double Atan(double x)
    {
        int hx = Hi(x);
        int ix = hx & 0x7fffffff;
        int id;
        if (ix >= 0x44100000)
        {
            if (double.IsNaN(x))
                return x + x;
            return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
        }
        if (ix < 0x3fdc0000)
        {
            if (ix < 0x3e400000)
                return x;
            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3ff30000)
            {
                if (ix < 0x3fe60000)
                {
                    id = 0;
                    x = (2.0 * x - 1.0) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1.0) / (x + 1.0);
                }
            }
            else if (ix < 0x40038000)
            {
                id = 2;
                x = (x - 1.5) / (1.0 + 1.5 * x);
            }
            else
            {
                id = 3;
                x = -1.0 / x;
            }
        }
        double z = x * x;
        double w = z * z;
        double s1 = z * (AT[0] + w * (AT[2] + w * (AT[4] + w * (AT[6] + w * (AT[8] + w * AT[10])))));
        double s2 = w * (AT[1] + w * (AT[3] + w * (AT[5] + w * (AT[7] + w * AT[9]))));
        if (id < 0)
            return x - x * (s1 + s2);
        z = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
        return hx < 0 ? -z : z;
    }

    public static double Atan2(double y, double x)
    {
        const double tiny = 1.0e-300, pi_o_2 = 1.5707963267948965580E+00,
            pi = 3.1415926535897931160E+00, pi_lo = 1.2246467991473531772E-16;
        if (double.IsNaN(x) || double.IsNaN(y))
            return x + y;
        int hx = Hi(x), ix = hx & 0x7fffffff;
        uint lx = Lo(x);
        int hy = Hi(y), iy = hy & 0x7fffffff;
        uint ly = Lo(y);
        if (((hx - 0x3ff00000) | (int)lx) == 0)
            return Atan(y);
        int m = ((hy >> 31) & 1) | ((hx >> 30) & 2);
        if ((iy | (int)ly) == 0)
        {
            return m switch
            {
                0 or 1 => y,
                2 => pi + tiny,
                _ => -pi - tiny,
            };
        }
        if ((ix | (int)lx) == 0)
            return hy < 0 ? -pi_o_2 - tiny : pi_o_2 + tiny;
        if (ix == 0x7ff00000 || iy == 0x7ff00000)
            return Math.Atan2(y, x);
        int k = (iy - ix) >> 20;
        double z;
        if (k > 60)
        {
            z = pi_o_2 + 0.5 * pi_lo;
            m &= 1;
        }
        else if (hx < 0 && k < -60)
        {
            z = 0.0;
        }
        else
        {
            z = Atan(Math.Abs(y / x));
        }
        return m switch
        {
            0 => z,
            1 => -z,
            2 => pi - (z - pi_lo),
            _ => (z - pi_lo) - pi,
        };
    }

    public static double Exp(double x)
    {
        const double ln2HI = 6.93147180369123816490e-01, ln2LO = 1.90821492927058770002e-10,
            invln2 = 1.44269504088896338700e+00,
            P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03,
            P3 = 6.61375632143793436117e-05, P4 = -1.65339022054652515390e-06,
            P5 = 4.13813679705723846039e-08;
        int hx = Hi(x);
        int xsb = (hx >> 31) & 1;
        hx &= 0x7fffffff;
        // Overflow, underflow, NaN and infinity: nothing the generator meets.
        if (hx >= 0x40862E42)
            return Math.Exp(x);
        double hi = 0, lo = 0;
        int k = 0;
        if (hx > 0x3fd62e42)
        {
            if (hx < 0x3FF0A2B2)
            {
                hi = x - (xsb == 0 ? ln2HI : -ln2HI);
                lo = xsb == 0 ? ln2LO : -ln2LO;
                k = 1 - xsb - xsb;
            }
            else
            {
                k = (int)(invln2 * x + (xsb == 0 ? 0.5 : -0.5));
                double t0 = k;
                hi = x - t0 * ln2HI;
                lo = t0 * ln2LO;
            }
            x = hi - lo;
        }
        else if (hx < 0x3e300000)
        {
            return 1.0 + x;
        }
        double t = x * x;
        double twopk = k >= -1021 ? Words(0x3ff00000 + (k << 20), 0) : Words(0x3ff00000 + ((k + 1000) << 20), 0);
        double c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        if (k == 0)
            return 1.0 - ((x * c) / (c - 2.0) - x);
        double y = 1.0 - ((lo - (x * c) / (2.0 - c)) - hi);
        if (k >= -1021)
            return k == 1024 ? y * 2.0 * 8.98846567431157953865e+307 : y * twopk;
        return y * twopk * 9.33263618503218878990e-302;
    }
}
