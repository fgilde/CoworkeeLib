<p align="center">
  <img src="assets/icon.svg" width="120" alt="Coworkee logo" />
</p>

<h1 align="center">Coworkee</h1>

<p align="center">A small, modular application framework for .NET 10.<br/>Modules, a request dispatcher, EF Core conventions with audit and outbox, ASP.NET Core and Aspire integration.</p>

<p align="center"><a href="https://www.nuget.org/packages?q=Coworkee"><img src="https://img.shields.io/nuget/v/Coworkee.Core?logo=nuget&amp;label=NuGet&amp;color=004880" alt="NuGet" /></a> <a href="https://framework.coworkee.de"><img src="https://img.shields.io/badge/docs-framework.coworkee.de-1565c0?logo=data:image%2Fsvg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHhtbG5zOnhsaW5rPSJodHRwOi8vd3d3LnczLm9yZy8xOTk5L3hsaW5rIiB2aWV3Qm94PSIwIDAgMTA4LjEgMTA4LjEiPjxkZWZzPjxzdHlsZT4uY2xzLTF7ZmlsbDp1cmwoI2kxMyk7fS5jbHMtMntmaWxsOnVybCgjaTUpO30uY2xzLTN7ZmlsbDp1cmwoI2k2KTt9LmNscy00e2ZpbGw6dXJsKCNpOCk7fS5jbHMtNXtmaWxsOnVybCgjaTEyKTt9LmNscy02e2ZpbGw6dXJsKCNpNyk7fS5jbHMtN3tmaWxsOnVybCgjaTEwKTt9LmNscy04e2ZpbGw6dXJsKCNpMTEpO30uY2xzLTl7ZmlsbDp1cmwoI2k5KTt9LmNscy0xMHtmaWxsOm5vbmU7Y2xpcC1ydWxlOmV2ZW5vZGQ7fS5jbHMtMTF7Y2xpcC1wYXRoOnVybCgjaTE4KTt9LmNscy0xMntmaWxsOnVybCgjaTQpO30uY2xzLTEze2NsaXAtcGF0aDp1cmwoI2kxNik7fS5jbHMtMTR7ZmlsbDp1cmwoI2kzKTt9LmNscy0xNXtjbGlwLXBhdGg6dXJsKCNpMTcpO30uY2xzLTE2e2ZpbGw6dXJsKCNpMSk7fS5jbHMtMTd7bWFzazp1cmwoI2kyOCk7fS5jbHMtMTh7ZmlsbDojZjlkOTJlO30uY2xzLTE4LC5jbHMtMjIsLmNscy0yNCwuY2xzLTI2LC5jbHMtMjgsLmNscy0zMCwuY2xzLTMyLC5jbHMtMzZ7ZmlsbC1ydWxlOmV2ZW5vZGQ7fS5jbHMtMTl7Y2xpcC1wYXRoOnVybCgjaTE0KTt9LmNscy0yMHtmaWxsOnVybCgjaTApO30uY2xzLTIxe21hc2s6dXJsKCNpMjYpO30uY2xzLTIye2ZpbGw6I2ZlZjE2Mzt9LmNscy0yM3ttYXNrOnVybCgjaTI0KTt9LmNscy0yNHtmaWxsOiNmOThhZmU7fS5jbHMtMjV7bWFzazp1cmwoI2kyNyk7fS5jbHMtMjZ7ZmlsbDojMzAwYzUwO30uY2xzLTI3e21hc2s6dXJsKCNpMjIpO30uY2xzLTI4e2ZpbGw6IzAwNWI1MDt9LmNscy0yOXttYXNrOnVybCgjaTIzKTt9LmNscy0zMHtmaWxsOiNmZWY3YTI7fS5jbHMtMzF7bWFzazp1cmwoI2kyNSk7fS5jbHMtMzJ7ZmlsbDojZjZkY2ZkO30uY2xzLTMze2NsaXAtcGF0aDp1cmwoI2kxNSk7fS5jbHMtMzR7ZmlsbDp1cmwoI2kyKTt9LmNscy0zNXttYXNrOnVybCgjaTIxKTt9LmNscy0zNntmaWxsOiMwMTA1MmQ7fTwvc3R5bGU%2BPGxpbmVhckdyYWRpZW50IGlkPSJpMTMiIHgxPSItMTcyLjYiIHkxPSItNTEuMSIgeDI9Ii0xNzIuNiIgeTI9IjI1OC44IiBncmFkaWVudFRyYW5zZm9ybT0idHJhbnNsYXRlKDIyNi43IC0xNTMuNikiIGdyYWRpZW50VW5pdHM9InVzZXJTcGFjZU9uVXNlIj48c3RvcCBvZmZzZXQ9IjAiIHN0b3AtY29sb3I9IiNmZmYiLz48c3RvcCBvZmZzZXQ9IjEiLz48L2xpbmVhckdyYWRpZW50PjxsaW5lYXJHcmFkaWVudCBpZD0iaTUiIHgxPSItMTk5LjciIHkxPSIyMzkuMCIgeDI9Ii0xOTkuNyIgeTI9IjI2Ni43IiBncmFkaWVudFRyYW5zZm9ybT0idHJhbnNsYXRlKDI4MC44IC0yNDAuNSkiIHhsaW5rOmhyZWY9IiNpMTMiLz48bGluZWFyR3JhZGllbnQgaWQ9Imk2IiB4MT0iLTE5OS43IiB5MT0iMjYxLjgiIHgyPSItMTk5LjciIHkyPSIyMzQuOSIgZ3JhZGllbnRUcmFuc2Zvcm09InRyYW5zbGF0ZSgyODAuOCAtMTUzLjYpIiB4bGluazpocmVmPSIjaTEzIi8%2BPGxpbmVhckdyYWRpZW50IGlkPSJpOCIgeDE9Ii0zMDkuNiIgeTE9IjM0Ny45IiB4Mj0iLTE3OC40IiB5Mj0iMTg3LjYiIGdyYWRpZW50VHJhbnNmb3JtPSJ0cmFuc2xhdGUoMjM5LjcgLTE1My42KSIgeGxpbms6aHJlZj0iI2kxMyIvPjxsaW5lYXJHcmFkaWVudCBpZD0iaTEyIiB4MT0iLTI5Ny4wIiB5MT0iMzEwLjgiIHgyPSItMTY2LjkiIHkyPSIyMzUuNyIgZ3JhZGllbnRUcmFuc2Zvcm09InRyYW5zbGF0ZSgyODAuOSAtMjMwLjYpIiB4bGluazpocmVmPSIjaTEzIi8%2BPGxpbmVhckdyYWRpZW50IGlkPSJpNyIgeDE9Ii04Ni4xIiB5MT0iNTEuMiIgeDI9Ii0yMjcuOSIgeTI9IjI5Mi4zIiBncmFkaWVudFRyYW5zZm9ybT0idHJhbnNsYXRlKDIzOC42IC0xNTUuMCkiIHhsaW5rOmhyZWY9IiNpMTMiLz48bGluZWFyR3JhZGllbnQgaWQ9ImkxMCIgeDE9Ii00Mi4wIiB5MT0iMzAwLjIiIHgyPSItMjI1LjIiIHkyPSIxOTguNyIgZ3JhZGllbnRUcmFuc2Zvcm09InRyYW5zbGF0ZSgyNTEuNSAtMTUzLjYpIiB4bGluazpocmVmPSIjaTEzIi8%2BPGxpbmVhckdyYWRpZW50IGlkPSJpMTEiIHgxPSItMTcyLjYiIHkxPSI0NjYuNiIgeDI9Ii0xNzIuNiIgeTI9IjE1Ni42IiB4bGluazpocmVmPSIjaTEzIi8%2BPGxpbmVhckdyYWRpZW50IGlkPSJpOSIgeDE9Ii0zMjUuNSIgeTE9IjE4Ni40IiB4Mj0iLTE3MS43IiB5Mj0iMjc1LjIiIGdyYWRpZW50VHJhbnNmb3JtPSJ0cmFuc2xhdGUoMjgwLjkgLTE3NC45KSIgeGxpbms6aHJlZj0iI2kxMyIvPjxjbGlwUGF0aCBpZD0iaTE4Ij48cGF0aCBjbGFzcz0iY2xzLTEwIiBkPSJNNzQuNCw3My4zYTI4LDI4LDAsMCwxLTIwLjIsOC43aDI4QTI1LjgsMjUuOCwwLDAsMSw5Nyw4Ni44YTUzLjgsNTMuOCwwLDAsMCw4LjYtMTYuNywyNiwyNiwwLDAsMC0zMS4yLDMuMloiLz48L2NsaXBQYXRoPjxsaW5lYXJHcmFkaWVudCBpZD0iaTQiIHgxPSIxMDQuMiIgeTE9IjUzLjIiIHgyPSI2OS40IiB5Mj0iODUuMSIgZ3JhZGllbnRVbml0cz0idXNlclNwYWNlT25Vc2UiPjxzdG9wIG9mZnNldD0iMCIgc3RvcC1jb2xvcj0iI2M4MzVmOCIvPjxzdG9wIG9mZnNldD0iMSIgc3RvcC1jb2xvcj0iIzAyNzZkYiIvPjwvbGluZWFyR3JhZGllbnQ%2BPGNsaXBQYXRoIGlkPSJpMTYiPjxwYXRoIGNsYXNzPSJjbHMtMTAiIGQ9Ik03NCwzNC4yYTI2LDI2LDAsMCwwLDMxLjcsMy44QTU0LDU0LDAsMCwwLDk3LDIxLjIsMjUuOCwyNS44LDAsMCwxLDgyLjEsMjZoLTI4QTI4LDI4LDAsMCwxLDc0LDM0LjJaIi8%2BPC9jbGlwUGF0aD48bGluZWFyR3JhZGllbnQgaWQ9ImkzIiB4MT0iMTAzLjEiIHkxPSI1NS43IiB4Mj0iNjcuNyIgeTI9IjIwLjUiIGdyYWRpZW50VW5pdHM9InVzZXJTcGFjZU9uVXNlIj48c3RvcCBvZmZzZXQ9IjAiIHN0b3AtY29sb3I9IiNmZjAiLz48c3RvcCBvZmZzZXQ9IjEiIHN0b3AtY29sb3I9IiMwN2FiYTAiLz48L2xpbmVhckdyYWRpZW50PjxjbGlwUGF0aCBpZD0iaTE3Ij48cGF0aCBjbGFzcz0iY2xzLTEwIiBkPSJNMTMsNjdBNDEsNDEsMCwwLDEsNTQsMjZIODIuMWEyNiwyNiwwLDAsMCwyNi0yNmgtNTRhNTQuMCw1NC4wLDAsMSwwLDAsMTA4LjF2LS4wNkE0MSw0MSwwLDAsMSwxMyw2NyIvPjwvY2xpcFBhdGg%2BPGxpbmVhckdyYWRpZW50IGlkPSJpMSIgeDE9IjEzLjkiIHkxPSI5NC42IiB4Mj0iMTIyLjkiIHkyPSItMTUuNiIgZ3JhZGllbnRVbml0cz0idXNlclNwYWNlT25Vc2UiPjxzdG9wIG9mZnNldD0iMCIgc3RvcC1jb2xvcj0iI2VkMzc2YSIvPjxzdG9wIG9mZnNldD0iMSIgc3RvcC1jb2xvcj0iI2ZmMCIvPjwvbGluZWFyR3JhZGllbnQ%2BPG1hc2sgaWQ9ImkyOCIgeD0iMCIgeT0iMCIgd2lkdGg9IjEwOC4xIiBoZWlnaHQ9IjEwOC4xIiBtYXNrVW5pdHM9InVzZXJTcGFjZU9uVXNlIj48cmVjdCBjbGFzcz0iY2xzLTEiIHdpZHRoPSIxMDguMSIgaGVpZ2h0PSIxMDguMSIvPjwvbWFzaz48Y2xpcFBhdGggaWQ9ImkxNCI%2BPHBhdGggY2xhc3M9ImNscy0xMCIgZD0iTTU0LjAsODIuMUEyOC4wLDI4LjAsMCwwLDEsNTQsMjZhNDEsNDEsMCwwLDAsLjExLDgyLjB2LjA2aDU0LjBhMjYsMjYsMCwwLDAtMjYtMjZaIi8%2BPC9jbGlwUGF0aD48bGluZWFyR3JhZGllbnQgaWQ9ImkwIiB4MT0iMjMuMCIgeTE9IjQ5LjEiIHgyPSIxMTUuMSIgeTI9IjkzLjEiIGdyYWRpZW50VW5pdHM9InVzZXJTcGFjZU9uVXNlIj48c3RvcCBvZmZzZXQ9IjAiIHN0b3AtY29sb3I9IiNlZDM3NmEiLz48c3RvcCBvZmZzZXQ9IjEiIHN0b3AtY29sb3I9IiNjODM1ZjgiLz48L2xpbmVhckdyYWRpZW50PjxtYXNrIGlkPSJpMjYiIHg9IjU0LjEiIHk9IjAiIHdpZHRoPSI1My45IiBoZWlnaHQ9IjIxLjIiIG1hc2tVbml0cz0idXNlclNwYWNlT25Vc2UiPjxyZWN0IGNsYXNzPSJjbHMtMiIgeD0iNTQuMSIgd2lkdGg9IjUzLjkiIGhlaWdodD0iMjEuMiIvPjwvbWFzaz48bWFzayBpZD0iaTI0IiB4PSI1NC4xIiB5PSI4Ni44IiB3aWR0aD0iNTMuOSIgaGVpZ2h0PSIyMS4yIiBtYXNrVW5pdHM9InVzZXJTcGFjZU9uVXNlIj48cmVjdCBjbGFzcz0iY2xzLTMiIHg9IjU0LjEiIHk9Ijg2LjgiIHdpZHRoPSI1My45IiBoZWlnaHQ9IjIxLjIiLz48L21hc2s%2BPG1hc2sgaWQ9ImkyNyIgeD0iMTMuMCIgeT0iMjUuOSIgd2lkdGg9IjQyLjQiIGhlaWdodD0iODIuMSIgbWFza1VuaXRzPSJ1c2VyU3BhY2VPblVzZSI%2BPHJlY3QgY2xhc3M9ImNscy00IiB4PSIxMy4wIiB5PSIyNS45IiB3aWR0aD0iNDIuNCIgaGVpZ2h0PSI4Mi4xIi8%2BPC9tYXNrPjxtYXNrIGlkPSJpMjIiIHg9IjU0LjEiIHk9IjIxLjIiIHdpZHRoPSI0Ni40IiBoZWlnaHQ9IjkuOCIgbWFza1VuaXRzPSJ1c2VyU3BhY2VPblVzZSI%2BPHJlY3QgY2xhc3M9ImNscy01IiB4PSI1NC4xIiB5PSIyMS4yIiB3aWR0aD0iNDYuNCIgaGVpZ2h0PSI5LjgiLz48L21hc2s%2BPG1hc2sgaWQ9ImkyMyIgeD0iMTEuOCIgeT0iMCIgd2lkdGg9Ijk2LjIiIGhlaWdodD0iMTA2LjciIG1hc2tVbml0cz0idXNlclNwYWNlT25Vc2UiPjxyZWN0IGNsYXNzPSJjbHMtNiIgeD0iMTEuOCIgd2lkdGg9Ijk2LjIiIGhlaWdodD0iMTA2LjciLz48L21hc2s%2BPG1hc2sgaWQ9ImkyNSIgeD0iMjQuOCIgeT0iMjYuNCIgd2lkdGg9IjgzLjIiIGhlaWdodD0iODEuNiIgbWFza1VuaXRzPSJ1c2VyU3BhY2VPblVzZSI%2BPHJlY3QgY2xhc3M9ImNscy03IiB4PSIyNC44IiB5PSIyNi40IiB3aWR0aD0iODMuMiIgaGVpZ2h0PSI4MS42Ii8%2BPC9tYXNrPjxjbGlwUGF0aCBpZD0iaTE1Ij48cGF0aCBjbGFzcz0iY2xzLTEwIiBkPSJNMTA4LjEsMTA4LjFoLTU0QTU0LjAsNTQuMCwwLDEsMSw1NC4wLDBhNDcuNSw0Ny41LDAsMCwwLDAsOTUuMUg5NS4xYTEzLDEzLDAsMCwxLDEzLDEzIi8%2BPC9jbGlwUGF0aD48bGluZWFyR3JhZGllbnQgaWQ9ImkyIiB4MT0iOTEuMiIgeTE9IjU0LjAiIHgyPSIxNy4yIiB5Mj0iNTQuMCIgZ3JhZGllbnRVbml0cz0idXNlclNwYWNlT25Vc2UiPjxzdG9wIG9mZnNldD0iMCIgc3RvcC1jb2xvcj0iIzVlMTdhOSIvPjxzdG9wIG9mZnNldD0iMSIgc3RvcC1jb2xvcj0iI2M0MDAxNSIvPjwvbGluZWFyR3JhZGllbnQ%2BPG1hc2sgaWQ9ImkyMSIgeD0iNTQuMSIgeT0iNzYuOSIgd2lkdGg9IjQ2LjQiIGhlaWdodD0iOS44IiBtYXNrVW5pdHM9InVzZXJTcGFjZU9uVXNlIj48cmVjdCBjbGFzcz0iY2xzLTkiIHg9IjU0LjEiIHk9Ijc2LjkiIHdpZHRoPSI0Ni40IiBoZWlnaHQ9IjkuOCIvPjwvbWFzaz48L2RlZnM%2BPGcgaWQ9ImkyMCIgZGF0YS1uYW1lPSJFYmVuZSAyIj48ZyBpZD0iaTE5IiBkYXRhLW5hbWU9IkxheWVyIDEiPjxnIGNsYXNzPSJjbHMtMTEiPjxyZWN0IGNsYXNzPSJjbHMtMTIiIHg9IjU0LjEiIHk9IjY0LjEiIHdpZHRoPSI1MS41IiBoZWlnaHQ9IjIyLjYiLz48L2c%2BPGcgY2xhc3M9ImNscy0xMyI%2BPHJlY3QgY2xhc3M9ImNscy0xNCIgeD0iNTQuMSIgeT0iMjEuMiIgd2lkdGg9IjUxLjUiIGhlaWdodD0iMjIuOCIvPjwvZz48ZyBjbGFzcz0iY2xzLTE1Ij48cmVjdCBjbGFzcz0iY2xzLTE2IiB3aWR0aD0iMTA4LjEiIGhlaWdodD0iMTA4LjEiLz48L2c%2BPGcgY2xhc3M9ImNscy0xNyI%2BPHBhdGggY2xhc3M9ImNscy0xOCIgZD0iTTEwOC4xLDBoLTU0YTU0LjAsNTQuMCwwLDEsMCwwLDEwOC4xLDQ3LjUsNDcuNSwwLDAsMSwwLTk1LjFIOTUuMWExMywxMywwLDAsMCwxMy0xMyIvPjwvZz48ZyBjbGFzcz0iY2xzLTE5Ij48cmVjdCBjbGFzcz0iY2xzLTIwIiB4PSIxMy4wIiB5PSIyNS45IiB3aWR0aD0iOTUuMCIgaGVpZ2h0PSI4Mi4xIi8%2BPC9nPjxnIGNsYXNzPSJjbHMtMjEiPjxwYXRoIGNsYXNzPSJjbHMtMjIiIGQ9Ik01NC4xLDBBNTQsNTQsMCwwLDEsOTcsMjEuMiwyNiwyNiwwLDAsMCwxMDguMSwwWiIvPjwvZz48ZyBjbGFzcz0iY2xzLTIzIj48cGF0aCBjbGFzcz0iY2xzLTI0IiBkPSJNOTcsODYuOGE1NCw1NCwwLDAsMS00Mi45LDIxLjJoNTRBMjYsMjYsMCwwLDAsOTcsODYuOCIvPjwvZz48ZyBjbGFzcz0iY2xzLTI1Ij48cGF0aCBjbGFzcz0iY2xzLTI2IiBkPSJNMTguMSw2OS42YTM4LjIsMzguMiwwLDAsMSw5LjYtMjUuM0EyOC4wLDI4LjAsMCwwLDEsNTQsMjZhNDEsNDEsMCwwLDAsLjExLDgyLjB2LjA2aC4wOGwxLjMsMEEzOC41LDM4LjUsMCwwLDEsMTguMSw2OS42Ii8%2BPC9nPjxnIGNsYXNzPSJjbHMtMjciPjxwYXRoIGNsYXNzPSJjbHMtMjgiIGQ9Ik03MC4yLDMxLjFoMTRhMzEsMzEsMCwwLDAsMTYuMy00LjZBNTUsNTUsMCwwLDAsOTcsMjEuMiwyNS44LDI1LjgsMCwwLDEsODIuMSwyNmgtMjhhMjgsMjgsMCwwLDEsMTYuMSw1LjEiLz48L2c%2BPGcgY2xhc3M9ImNscy0yOSI%2BPHBhdGggY2xhc3M9ImNscy0zMCIgZD0iTTU0LDI0LjhBNDIuMiw0Mi4yLDAsMCwwLDExLjgsNjdjMCwxMS43LDYuMCwzMy44LDMxLjgsMzkuN0E0MSw0MSwwLDAsMSw1NCwyNkg4Mi4xQTI1LjgsMjUuOCwwLDAsMCw5NywyMS4yLDI2LDI2LDAsMCwwLDEwOC4xLDBjLS4zOCw4LjEtNS4xLDE1LjctMTEuOCwyMC4yYTI1LjIsMjUuMiwwLDAsMS0xNC4xLDQuNUg1NCIvPjwvZz48ZyBjbGFzcz0iY2xzLTMxIj48cGF0aCBjbGFzcz0iY2xzLTMyIiBkPSJNNTQuMCw4Mi4xYTI4LjAsMjguMCwwLDAsMS01LjItNTUuNmMtMTguOCwzLjEtMjQsMTkuNS0yNCwyNy41QTI5LjIsMjkuMiwwLDAsMCw1NC4wLDgzLjJIODIuMWMxMy4zLDAsMjUuMSwxMS4xLDI2LDI0LjhhMjYsMjYsMCwwLDAtMjYtMjZaIi8%2BPC9nPjxnIGNsYXNzPSJjbHMtMzMiPjxnIGNsYXNzPSJjbHMtMTciPjxyZWN0IGNsYXNzPSJjbHMtMzQiIHdpZHRoPSIxMDguMSIgaGVpZ2h0PSIxMDguMSIvPjwvZz48L2c%2BPGcgY2xhc3M9ImNscy0zNSI%2BPHBhdGggY2xhc3M9ImNscy0zNiIgZD0iTTcwLjIsNzdoMTRhMzEsMzEsMCwwLDEsMTYuMyw0LjZBNTUsNTUsMCwwLDEsOTcsODYuOGEyNS44LDI1LjgsMCwwLDAtMTQuOS00LjdoLTI4QTI4LDI4LDAsMCwwLDcwLjIsNzciLz48L2c%2BPC9nPjwvZz48L3N2Zz4%3D" alt="Documentation: framework.coworkee.de" /></a> <a href="https://quickrun.org/run?repo=fgilde/CoworkeeLib"><img src="https://quickrun.org/badge.svg" alt="QuickRun" /></a></p>

---

**Documentation:** [framework.coworkee.de](https://framework.coworkee.de/) (English, [Deutsch](https://framework.coworkee.de/de/)) · **Demo app:** [fgilde/Coworkee](https://github.com/fgilde/Coworkee/tree/master), a complete application on the packages

```bash
dotnet tool install -g Coworkee.Cli
coworkee new MyApp
```

## Packages

| Package | What it gives you |
|---|---|
| `Coworkee.Core` | Module system with `[DependsOn]`, `Result` / `Error`, current user and ambient user scope |
| `Coworkee.Domain` | `Entity`, `AggregateRoot` with domain events, markers for audit, soft delete, tenants and concurrency |
| `Coworkee.Application` | Dispatcher for commands and queries with logging, validation and unit-of-work middleware |
| `Coworkee.Infrastructure` | `CoworkeeDbContext`, interceptors, field-level audit trail, transactional outbox with retries |
| `Coworkee.AspNetCore` | `AddCoworkee<TRoot>()`, result to problem-details mapping, API groups, bearer authentication, OpenAPI and Swagger UI |
| `Coworkee.OData` | OData set per entity with facets, row filters and hidden properties |
| `Coworkee.ResponseFilters` | Permission dependent response filters on Nextended.ResponseFilters |
| `Coworkee.Testing` | Postgres test container fixture, test user, web application factory |
| `Coworkee.Aspire` | `AddCoworkeeApp()` wires migrations, auth server, APIs, workers and the web host with Postgres, Redis, Mailpit, Elasticsearch and Keycloak |
| `Coworkee.Contracts` | DTOs, permission names and paging records shared by server and clients |
| `Coworkee.Identity` | Users, roles, groups, tenants and permission grants with resource level checks |
| `Coworkee.AuthServer` / `Coworkee.Account` | OpenIddict server, login, registration, two factor and account pages |
| `Coworkee.Bff` | Backend for frontend: cookie session, token handling, API forwarding |
| `Coworkee.Settings` | Typed settings per global, tenant and user scope with encrypted secrets |
| `Coworkee.Mailing` | Scriban mail templates with overrides, queued SMTP delivery |
| `Coworkee.Notifications` / `Coworkee.Realtime` | In app notifications, digests and SignalR push |
| `Coworkee.BackgroundJobs` | Hangfire jobs with queues, retries and delayed follow-ups |
| `Coworkee.Storage` | Blob storage on the file system, S3 or Azure |
| `Coworkee.Search` / `Coworkee.Search.Elasticsearch` | Search index abstraction and Elasticsearch provider |
| `Coworkee.Auditing` | Audit trail queries and history views |
| `Coworkee.Theming` | Tenant themes and branding |
| `Coworkee.Ai` | Assistant chat with Claude over the tools of all modules, MCP server, tool call audit |
| `Coworkee.Client.Blazor` | MudBlazor shell, navigation, settings and admin pages for WebAssembly clients |

## Example

```csharp
[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class OrdersModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(OrdersModule).Assembly);
}

public sealed record PlaceOrder(string Product) : ICommand<Result<Guid>>;

internal sealed class PlaceOrderHandler(AppDbContext db) : IHandler<PlaceOrder, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(PlaceOrder request, CancellationToken ct)
    {
        var order = Order.Place(request.Product);
        db.Orders.Add(order);
        return Task.FromResult<Result<Guid>>(order.Id);
    }
}
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddCoworkee<OrdersModule>();
var app = builder.Build();
app.UseCoworkee();
app.MapPost("/orders", (PlaceOrder cmd, IDispatcher d, CancellationToken ct) => d.SendAsync(cmd, ct).ToHttpResult());
app.Run();
```

The unit of work saves after a successful command, the audit trail records every changed field, and domain events leave through the outbox in the same transaction.

## Build

```bash
dotnet test --solution Coworkee.slnx   # needs Docker for the Postgres tests
pwsh build/pack-local.ps1              # packs into artifacts/nuget for local consumers
```

Apps that build against unreleased sources point a `nuget.config` source at `artifacts/nuget`.

## Release

Publishing a GitHub release (or pushing a tag `v1.2.3`) builds, tests and publishes all packages, the templates and the CLI with that version to nuget.org. The workflow `release.yml` signs in with nuget.org trusted publishing, so there is no API key secret; the repository variable `NUGET_USER` overrides the nuget.org account name (default `fgilde`).

## Used by

- [Coworkee demo app](https://github.com/fgilde/Coworkee/tree/master): catalog, documents, dashboard and administration, the former standalone template ([original](https://github.com/fgilde/Coworkee/tree/release/last-standalone)) rebuilt on the packages
- [Sharemee](https://github.com/fgilde/sharemee): assets, documents, knowledge base and intranet

## Built with

<a href="https://www.mudex.org"><img src="https://raw.githubusercontent.com/fgilde/MudBlazor.Extensions/main/MudExtension.png" height="48" alt="MudBlazor.Extensions" /></a>

- [MudBlazor.Extensions](https://www.mudex.org): object edit, file display and file manager, tree view, dialogs, theme editor
- [MudBlazor](https://mudblazor.com): the component library underneath
- [Nextended](https://github.com/fgilde/Nextended): facets, response filters and code generation
- [.NET Aspire](https://aspire.dev), [OpenIddict](https://openiddict.com), [Hangfire](https://www.hangfire.io)

## License

MIT · built by [gilde.org](https://gilde.org)
