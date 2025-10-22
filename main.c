extern "glfw3.dll" int glfwInit();
extern "glfw3.dll" GLFWwindow glfwCreateWindow(int width, int height, char title, GLFWmonitor monitor, GLFWwindow share);

int main(int argc)
{
    if (!glfwInit())
        return -1;
    GLFWwindow window = glfwCreateWindow(800, 600, "Hello GLFW", NULL, NULL);
    return 1;
}