
struct GLFWwindow;
struct GLFWmonitor;

extern "glfw3.dll" int glfwInit();
extern "glfw3.dll" GLFWwindow* glfwCreateWindow(int width, int height, char* title, GLFWmonitor* monitor, GLFWwindow* share);
extern "glfw3.dll" int glfwWindowShouldClose(GLFWwindow* window);
extern "glfw3.dll" void glfwSwapBuffers(GLFWwindow* window);
extern "glfw3.dll" void glfwPollEvents();

int main(int argc)
{
    if (!glfwInit())
        return -1;
    GLFWwindow* window = glfwCreateWindow(800, 600, "Hello GLFW", NULL, NULL);
    while (!glfwWindowShouldClose(window)) {
        glfwSwapBuffers(window);
        glfwPollEvents();
    }
    return 1;
}