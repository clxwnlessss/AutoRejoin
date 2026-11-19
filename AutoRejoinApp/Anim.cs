using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoRejoinApp;

// Анимации интерфейса 2.0: плавное появление страниц, плашек и блоков.
public static class Anim
{
	private static readonly IEasingFunction Ease = new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	};

	// Появление снизу вверх с проявлением: страницы, плашки.
	public static void SlideIn(UIElement element, double fromY = 18.0, double seconds = 0.32, double delay = 0.0)
	{
		if (element == null)
		{
			return;
		}
		if (!(element.RenderTransform is TranslateTransform move))
		{
			move = new TranslateTransform();
			element.RenderTransform = move;
		}
		TimeSpan begin = TimeSpan.FromSeconds(delay);
		DoubleAnimation fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(seconds * 0.8))
		{
			BeginTime = begin
		};
		DoubleAnimation slide = new DoubleAnimation(fromY, 0.0, TimeSpan.FromSeconds(seconds))
		{
			BeginTime = begin,
			EasingFunction = Ease
		};
		element.Opacity = 0.0;
		move.Y = fromY;
		element.BeginAnimation(UIElement.OpacityProperty, fade);
		move.BeginAnimation(TranslateTransform.YProperty, slide);
	}

	// Короткая «вспышка» текста при смене значения (статистика сверху).
	public static void Pulse(UIElement element)
	{
		if (element == null)
		{
			return;
		}
		DoubleAnimation pulse = new DoubleAnimation(0.35, 1.0, TimeSpan.FromSeconds(0.4))
		{
			EasingFunction = Ease
		};
		element.BeginAnimation(UIElement.OpacityProperty, pulse);
	}
}
