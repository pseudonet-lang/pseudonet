**Edit: I explained this badly.** Several comments compare Pseudo.NET with Python, so here is what each one is.

**Python** is a programming language with one precise syntax, and that precision is a feature: the colons, the indentation and the keywords are what let the computer run the program exactly. For example, the sum of the multiples of 3 or 5 below 100:

```python
total = 0
for n in range(1, 100):
    if n % 3 == 0 or n % 5 == 0:
        total += n
print(total)
```

Get one character wrong and it does not run. That is the right trade-off for a programming language, and I am not suggesting anyone replace it.

**Pseudo.NET** is not a programming language to learn. It is for the step before: writing an algorithm in your own words, in any language, and running it. There is no fixed syntax. A local language model reads the text, turns it into a structured program, and that is compiled to C# like any other program. Here is the same program, written four different ways. All four were compiled with the published version and print the same result as the Python version above (2318):

English, with indentation only:
```
total = 0
repeat number from 1 to 99
	if number modulo 3 is equal to 0 or number modulo 5 is equal to 0 then
		total = total + number
print total
```

English, as plain sentences:
```
Let the total start at zero.
For every number from 1 up to 99, if the number can be divided by 3 or by 5, add it to the total.
At the end, show the total.
```

Spanish, as plain sentences:
```
Empieza con un total de cero. Recorre los números del 1 al 99 y, cuando uno sea múltiplo de 3 o de 5, súmalo al total. Al final, muestra el total.
```

French, with indentation only:
```
total = 0
répéter nombre de 1 à 99
	si nombre est divisible par 3 ou par 5 alors
		total = total + nombre
afficher total
```

It can also produce a visual result in the same free-form way, with no library to import and no interface code: a window, shapes, keyboard input and a timer are described in plain words. This is a bouncing ball, as shipped in the samples (Windows only for now):

```
window 400 by 300
title "Bounce"
background black

ballX = 200
ballY = 150
speedX = 3
speedY = 2
ballRadius = 15

every 30 milliseconds {
    ballX = ballX + speedX
    ballY = ballY + speedY
    if ballX - ballRadius < 0 or ballX + ballRadius > width then speedX = -speedX
    if ballY - ballRadius < 0 or ballY + ballRadius > height then speedY = -speedY
    clear
    draw filled circle at (ballX, ballY) radius ballRadius color red
}
```

Snake and Tetris are written the same way. The gallery shows each one next to its pseudocode, the running game and the C# it became: https://pseudonet-lang.github.io/gallery/

So if Python already does the job in your class, you probably do not need this. One commenter described a case I had not thought of: teachers preparing a computer science certification, where pseudocode itself is assessed and they keep fighting its syntax. That is the kind of situation where being able to write pseudocode freely, and still run it to check it, seems most useful.

One honest note: a small local model is not perfect. These exact examples failed yesterday (none of the six variants I tried compiled), which is how I found a bug in how the compiler handles "divisible by"; the fix is in the current release (Visual Studio Marketplace 0.4.4, `dotnet new install PseudoNet.Templates` 0.1.2). If you find a phrasing that does not work, please tell me, it is the most useful feedback I can get: https://github.com/pseudonet-lang/pseudonet/discussions
