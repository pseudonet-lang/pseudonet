**Edit: I explained this badly.** Several comments compare Pseudo.NET with Python, so here is what each one is.

**Python** is a programming language with one precise syntax, and that precision is a feature: the colons, the indentation and the keywords are what let the computer run the program exactly. For example, the sum of the multiples of 3 or 5 below 100:

```python
total = 0
for n in range(1, 100):
    if n % 3 == 0 or n % 5 == 0:
        total += n
print(total)
```

Get one character wrong and it does not run. Good for a programming course, and Python is what I would suggest for that.

**Pseudo.NET** is meant for the step before: writing an algorithm in your own words, in any language, and running it. There is no fixed syntax. A local language model reads the text, turns it into a structured program, and that is compiled to C# like any other program. Here is the same program, written six different ways. All six were compiled and print the same result as the Python version above (2318):

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

Spanish, with indentation only:
```
total = 0
para cada número desde 1 hasta 99
	si el número es divisible entre 3 o entre 5 entonces
		total = total + número
mostrar total
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

French, as plain sentences:
```
On part d'un total de zéro. Pour chaque nombre de 1 à 99, si le nombre est multiple de 3 ou de 5, on l'ajoute au total. À la fin, on affiche le total.
```

So this is not a replacement for Python. If Python already does the job in your class, you probably do not need it. It is for the cases where writing pseudocode is the point (by hand, in another language than English, before the syntax gets in the way) and you want to be able to run what you wrote.

One honest note: a small local model is not perfect. These exact examples failed a few days ago, which is how I found a bug and fixed it. If you find a phrasing that does not work, please tell me, it is the most useful feedback I can get: https://github.com/pseudonet-lang/pseudonet/discussions
