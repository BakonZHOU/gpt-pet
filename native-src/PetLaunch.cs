using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CodexPetPlugin;

public sealed partial class PetWindow
{
    private enum LaunchPhase { Idle, Flying, Falling, Resting, Returning }
    private LaunchPhase launchPhase;
    private readonly DispatcherTimer launchTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch launchClock = new();
    private readonly Stopwatch holdClock = new();
    private readonly RotateTransform flightRotation = new();
    private readonly ScaleTransform flightSquash = new(1, 1);
    private readonly RotateTransform aimRotation = new();
    private readonly Polygon chargeArrow = new()
    {
        Points = new PointCollection { new(0,-4), new(34,-4), new(34,-10), new(50,0), new(34,10), new(34,4), new(0,4) },
        Fill = Brushes.Red, IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    private FrameworkElement launchPet = null!;
    private Vector velocity;
    private double spin, aim, previousFrame, restAt, returnAt, returnLeft, returnTop, returnAngle;
    private int impacts;
    private double landingGravity;
    private bool ChargeReady => holdClock.IsRunning && holdClock.Elapsed.TotalSeconds >= 1.22;

    private void SetupLaunch(FrameworkElement pet)
    {
        launchPet = pet;
        breathe.CenterY = pet.Height / 2;
        var transforms = new TransformGroup();
        transforms.Children.Add(breathe); transforms.Children.Add(flightSquash); transforms.Children.Add(flightRotation);
        pet.RenderTransform = transforms; pet.RenderTransformOrigin = new Point(.5, .5);
        chargeArrow.RenderTransform = aimRotation;
        // Chest position in the original 375-unit widget coordinates.
        Canvas.SetLeft(chargeArrow, Base * .705); Canvas.SetTop(chargeArrow, Base * .91);
        root.Children.Add(chargeArrow);
        launchTimer.Tick += (_, _) => TickLaunch();
    }
    private void BeginCharge()
    {
        holdClock.Restart(); launchTimer.Start();
    }
    private void CancelCharge()
    {
        holdClock.Reset(); chargeArrow.Visibility = Visibility.Collapsed;
        if (launchPhase == LaunchPhase.Idle) launchTimer.Stop();
    }
    private void TickLaunch()
    {
        if (launchPhase == LaunchPhase.Idle)
        {
            if (!ChargeReady) return;
            StopAlerts(); HideBubble(true); menu.IsOpen = false;
            double energy = Math.Clamp((holdClock.Elapsed.TotalSeconds - 1.22) / 13.78, 0, 1);
            squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            squeeze.ScaleX = 1.05 - energy * .46; squeeze.ScaleY = .88 - energy * .4;
            var cursor = CursorPosition();
            Point chest = launchPet.PointToScreen(new Point(launchPet.ActualWidth / 2, launchPet.ActualHeight * .85));
            aim = Math.Atan2(cursor.Y - chest.Y, cursor.X - chest.X);
            chargeArrow.Visibility = Visibility.Visible;
            aimRotation.Angle = aim * 180 / Math.PI * (settings.Mirror ? -1 : 1) + (settings.Mirror ? 180 : 0);
            return;
        }
        double elapsed = launchClock.Elapsed.TotalSeconds;
        double delta = Math.Min(.04, elapsed - previousFrame); previousFrame = elapsed;
        Rect bounds = SystemParameters.WorkArea;
        double floorBottom = TaskbarAvoidance.Read(this).AvailableBottom;
        double size = Width * WhaleFraction;
        double inset = Width - size;
        double radians = flightRotation.Angle * Math.PI / 180;
        double edgeInset = size * .5 * (1 - .7 * (Math.Abs(Math.Cos(radians)) + Math.Abs(Math.Sin(radians))));
        double leftEdge = bounds.Left - inset - edgeInset;
        double rightEdge = Math.Max(leftEdge, bounds.Right - Width + edgeInset);
        double floor = floorBottom - Height + edgeInset;
        if (launchPhase == LaunchPhase.Flying)
        {
            // Substeps keep fast launches from tunnelling through screen edges.
            int steps = Math.Max(1, (int)Math.Ceiling(delta / .004));
            for (int i = 0; i < steps; i++)
            {
                double dt = delta / steps;
                velocity.Y += 1500 * dt;
                velocity *= Math.Exp(-.28 * dt);
                double x = Left + velocity.X * dt, y = Top + velocity.Y * dt;
                bool hitX = false, hitY = false;
                double minX = leftEdge, maxX = rightEdge;
                double minY = bounds.Top - inset - edgeInset, maxY = Math.Max(minY, floor);
                if (x < minX || x > maxX) { x = Math.Clamp(x, minX, Math.Max(minX,maxX)); velocity.X *= -.8; hitX = true; }
                if (y < minY || y > maxY) { y = Math.Clamp(y, minY, maxY); velocity.Y *= -.74; hitY = true; }
                Left = x; Top = y;
                if (hitX || hitY) { impacts++; spin *= .8; ImpactSquash(hitX); }
                if (hitY && y >= maxY - .1 && velocity.Length < 180)
                { LandPet(maxY, elapsed); break; }
                if (elapsed >= 4.2 || impacts >= 8)
                {
                    // Spend the final fraction of flight actually falling, rather than snapping to the floor.
                    launchPhase = LaunchPhase.Falling; velocity.Y = Math.Max(0, velocity.Y);
                    landingGravity = Math.Max(1500, 2 * Math.Max(0, maxY - Top) / (.55 * .55));
                    break;
                }
            }
            flightRotation.Angle += spin * delta; spin *= Math.Exp(-.24 * delta);
        }
        else if (launchPhase == LaunchPhase.Falling)
        {
            velocity.Y += landingGravity * delta; velocity.X *= Math.Exp(-6 * delta); spin *= Math.Exp(-3 * delta);
            Left = Math.Clamp(Left + velocity.X * delta, leftEdge, rightEdge);
            Top = Math.Min(floor, Top + velocity.Y * delta);
            flightRotation.Angle += spin * delta;
            if (Top >= floor - .1) { ImpactSquash(false); LandPet(floor, elapsed); }
        }
        else if (launchPhase == LaunchPhase.Resting && elapsed - restAt >= 2)
        {
            launchPhase = LaunchPhase.Returning; returnAt = elapsed;
            returnLeft = Left; returnTop = Top; returnAngle = flightRotation.Angle % 360;
            if (returnAngle > 180) returnAngle -= 360;
        }
        else if (launchPhase == LaunchPhase.Returning)
        {
            double t = Math.Clamp((elapsed - returnAt) / .65, 0, 1), ease = t*t*(3-2*t);
            Left = returnLeft + (bounds.Right - Width - returnLeft) * ease;
            Top = returnTop + (TaskbarAvoidance.Read(this).AvailableBottom - Height - returnTop) * ease;
            flightRotation.Angle = returnAngle * (1-ease);
            flightSquash.ScaleX = flightSquash.ScaleY = .7 + .3*ease;
            if (t >= 1)
            {
                launchPhase = LaunchPhase.Idle; launchTimer.Stop(); flightRotation.Angle = 0;
                flightSquash.ScaleX = flightSquash.ScaleY = 1; bottomGap = 0; taskbarTarget = null;
                settings.Left = Left; settings.Top = Top; settings.BottomGap = 0; settings.Save(); SetAnimation();
            }
        }
    }
    private void FirePet(double? testEnergy = null)
    {
        double energy = testEnergy ?? Math.Clamp((holdClock.Elapsed.TotalSeconds - 1.22) / 13.78, 0, 1);
        CancelCharge(); StopSounds(); HideBubble(true); bottomGap = null; menuButton.Opacity = 0;
        settings.Mirror = false; ApplyMirror(false);
        squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        squeeze.ScaleX = squeeze.ScaleY = 1;
        breathe.BeginAnimation(ScaleTransform.ScaleXProperty, null); breathe.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        breathe.ScaleX = breathe.ScaleY = 1;
        flightSquash.ScaleX = flightSquash.ScaleY = .7;
        double speed = 650 + energy * 4500;
        velocity = new Vector(Math.Cos(aim)*speed, Math.Sin(aim)*speed);
        spin = (Random.Shared.Next(2)==0 ? -1 : 1) * (180 + energy*1500);
        impacts = 0; previousFrame = 0; launchPhase = LaunchPhase.Flying; launchClock.Restart(); launchTimer.Start();
    }
    private void ImpactSquash(bool horizontal)
    {
        // The rotation changes which local axis meets the wall.
        bool localX = horizontal == (Math.Abs(Math.Cos(flightRotation.Angle*Math.PI/180)) > .707);
        foreach (var (property, from) in new[] { (ScaleTransform.ScaleXProperty, localX ? .49 : .84), (ScaleTransform.ScaleYProperty, localX ? .84 : .49) })
            flightSquash.BeginAnimation(property, new DoubleAnimation(from, .7, TimeSpan.FromSeconds(.16)) { FillBehavior = FillBehavior.Stop });
    }
    private void LandPet(double floor, double elapsed)
    {
        Top = floor; velocity = new(); spin = 0; restAt = elapsed; launchPhase = LaunchPhase.Resting;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    private static Point CursorPosition() { GetCursorPos(out var point); return new(point.X, point.Y); }
}
