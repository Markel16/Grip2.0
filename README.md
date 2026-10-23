# Grip

## Pit stop

La escena `CIrcuito1` incluye un `PitStopManager` con dos zonas de trigger: entrada y salida. Sus posiciones y tamaño se ajustan desde el Inspector; selecciona el objeto para ver los gizmos (verde = entrada, cian = salida). Coloca ambas zonas sobre el carril de boxes.

Al entrar en boxes, frena hasta detenerte para iniciar el minijuego. Pulsa **Espacio**, **Enter** o toca el círculo cuando el pulso coincida con el anillo verde. Completa las cuatro ruedas; los errores añaden tiempo a la parada. Al terminar, se reinicia el desgaste y puedes conducir hasta la salida.

La IA toma una decisión de parada al alcanzar el desgaste configurado o la vuelta estratégica. Se desvía hacia la entrada, cambia los neumáticos automáticamente y vuelve al circuito por la salida.
